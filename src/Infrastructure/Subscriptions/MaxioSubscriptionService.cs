using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Production implementation of <see cref="ISubscriptionService"/> backed by
/// Maxio Advanced Billing as the billing system of record.
///
/// Idempotency guarantees:
/// - One Maxio customer per eShopOnWeb user: the Maxio customer carries the
///   eShopOnWeb user id as its unique <c>reference</c>; Maxio enforces
///   reference uniqueness, and the service looks the customer up before
///   creating one (and re-looks-up on the duplicate-reference race).
/// - One subscription per user+plan: the service checks its local mirror and
///   the live customer subscriptions in Maxio before enrolling, so repeated
///   subscribe calls (double-clicks, retries) return the same subscription.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly IRepository<BillingAccount> _accounts;
    private readonly IReadRepository<BillingAccount> _accountReads;
    private readonly IRepository<UserSubscription> _subscriptions;
    private readonly IReadRepository<UserSubscription> _subscriptionReads;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly MaxioSettings _settings;

    private const string SiteCurrencyCacheKey = "maxio.site.currency";
    private static readonly TimeSpan SiteCurrencyCacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> OpenStates = new(StringComparer.OrdinalIgnoreCase)
    { "active", "trialing", "past_due", "on_hold", "unpaid", "dunning", "scheduled" };

    public MaxioSubscriptionService(IMaxioClient maxio,
        IRepository<BillingAccount> accounts,
        IReadRepository<BillingAccount> accountReads,
        IRepository<UserSubscription> subscriptions,
        IReadRepository<UserSubscription> subscriptionReads,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _accounts = accounts;
        _accountReads = accountReads;
        _subscriptions = subscriptions;
        _subscriptionReads = subscriptionReads;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsInFamilyAsync(GetFamilyHandle(), cancellationToken);
        var currency = await GetSiteCurrencyAsync(cancellationToken);

        return products
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                Currency = currency,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, request.ProductHandle?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            if (!string.IsNullOrWhiteSpace(request.ProductHandle))
            {
                throw new SubscriptionPlanNotFoundException(request.ProductHandle);
            }
            plan = plans.OrderBy(p => p.Price).FirstOrDefault()
                ?? throw new SubscriptionPlanNotFoundException("(no plans published)");
        }

        var account = await EnsureCustomerAsync(request.UserId, request.Email, cancellationToken);

        // Idempotency: an already-open subscription to this plan is returned as-is.
        var subscriptionReference = SubscriptionReferenceFor(request.UserId, plan.Handle);
        var existing = await FindOpenSubscriptionAsync(request.UserId, account.MaxioCustomerId,
            plan.Handle, subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            existing.Created = false;
            return existing;
        }

        _logger.LogInformation("Enrolling user {UserId} in Maxio product {ProductHandle}",
            request.UserId, plan.Handle);

        MaxioSubscription created;
        bool createdHere = true;
        try
        {
            created = await _maxio.CreateSubscriptionAsync(plan.Handle, account.MaxioCustomerId,
                subscriptionReference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 &&
            ex.ResponseBody.Contains("Reference", StringComparison.OrdinalIgnoreCase))
        {
            // Lost a race for the same user+plan (e.g. a double-click on two
            // concurrent requests): Maxio enforces subscription reference
            // uniqueness, so fetch the winner.
            created = await _maxio.GetSubscriptionByReferenceAsync(subscriptionReference, cancellationToken)
                ?? throw ex;
            createdHere = false;
        }

        var record = await PersistMirrorAsync(request.UserId, account.MaxioCustomerId, created, plan.Handle, cancellationToken);

        return ToSummary(record, created: createdHere);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var records = await _subscriptionReads.ListAsync(new UserSubscriptionsByUserIdSpec(userId), cancellationToken);

        var account = await _accountReads.FirstOrDefaultAsync(new BillingAccountByUserIdSpec(userId), cancellationToken);
        if (records.Count == 0 && account is not null)
        {
            // The local mirror may have been lost (e.g. ephemeral database); recover
            // from the billing system of record.
            var live = await _maxio.ListCustomerSubscriptionsAsync(account.MaxioCustomerId, cancellationToken);
            var restored = new List<SubscriptionSummary>();
            foreach (var sub in live)
            {
                var handle = sub.Product?.Handle;
                if (string.IsNullOrEmpty(handle))
                {
                    continue;
                }
                var record = await PersistMirrorAsync(userId, account.MaxioCustomerId, sub, handle, cancellationToken);
                restored.Add(ToSummary(record, created: false));
            }
            return restored;
        }

        var summaries = new List<SubscriptionSummary>();
        foreach (var record in records)
        {
            var live = await _maxio.GetSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (live is null)
            {
                _logger.LogWarning(
                    "Maxio subscription {SubscriptionId} for user {UserId} could not be read; serving cached state",
                    record.MaxioSubscriptionId, userId);
            }
            else if (!string.Equals(live.State, record.State, StringComparison.OrdinalIgnoreCase)
                     || live.CurrentPeriodEndsAt != record.NextBillingDateUtc
                     || live.ProductPriceInCents != record.ProductPriceInCents)
            {
                record.UpdateFromBillingSystem(live.State, live.ProductPriceInCents,
                    string.IsNullOrWhiteSpace(live.Currency) ? record.Currency : live.Currency,
                    live.CurrentPeriodEndsAt);
                await _subscriptions.UpdateAsync(record, cancellationToken);
                await _subscriptions.SaveChangesAsync(cancellationToken);
            }

            summaries.Add(ToSummary(record, created: false));
        }
        return summaries;
    }

    private async Task<BillingAccount> EnsureCustomerAsync(string userId, string email,
        CancellationToken cancellationToken)
    {
        var existing = await _accountReads.FirstOrDefaultAsync(new BillingAccountByUserIdSpec(userId), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var reference = CustomerReferenceFor(userId);
        var maxioCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);

        if (maxioCustomer is null)
        {
            var (first, last) = DeriveName(email);
            try
            {
                maxioCustomer = await _maxio.CreateCustomerAsync(first, last, email, reference, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                // Lost a race creating the same reference; Maxio enforces
                // reference uniqueness, so fetch the winner.
                maxioCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken)
                    ?? throw ex;
            }
        }

        var account = new BillingAccount(userId, maxioCustomer.Id, reference);
        await _accounts.AddAsync(account, cancellationToken);
        try
        {
            await _accounts.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A concurrent request for the same user may have inserted the row
            // already; reload the winner instead of failing the subscribe.
            var winner = await _accountReads.FirstOrDefaultAsync(new BillingAccountByUserIdSpec(userId), cancellationToken);
            if (winner is null)
            {
                throw;
            }
            return winner;
        }
        return account;
    }

    private async Task<SubscriptionSummary?> FindOpenSubscriptionAsync(string userId, int maxioCustomerId,
        string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        // The billing system of record wins: the subscription reference is
        // unique per site in Maxio, so its lookup is authoritative even across
        // app restarts or concurrent requests.
        var byReference = await _maxio.GetSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (byReference is not null)
        {
            var persisted = await PersistMirrorAsync(userId, maxioCustomerId, byReference, planHandle, cancellationToken);
            if (IsOpen(persisted.State))
            {
                return ToSummary(persisted, created: false);
            }
        }

        var local = await _subscriptionReads.ListAsync(new UserSubscriptionsByUserIdSpec(userId), cancellationToken);

        foreach (var record in local)
        {
            if (record.MaxioSubscriptionId == byReference?.Id)
            {
                continue;
            }
            var live = await _maxio.GetSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (live is null)
            {
                continue;
            }
            if (!string.Equals(live.State, record.State, StringComparison.OrdinalIgnoreCase))
            {
                record.UpdateFromBillingSystem(live.State, live.ProductPriceInCents,
                    string.IsNullOrWhiteSpace(live.Currency) ? record.Currency : live.Currency,
                    live.CurrentPeriodEndsAt);
                await _subscriptions.UpdateAsync(record, cancellationToken);
                await _subscriptions.SaveChangesAsync(cancellationToken);
            }
            if (IsOpen(live.State)
                && string.Equals(live.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                return ToSummary(record, created: false);
            }
        }

        return null;
    }

    /// <summary>
    /// Mirrors a live Maxio subscription into the local store (upsert by
    /// Maxio subscription id).
    /// </summary>
    private async Task<UserSubscription> PersistMirrorAsync(string userId, int maxioCustomerId,
        MaxioSubscription live, string fallbackHandle, CancellationToken cancellationToken)
    {
        var existing = (await _subscriptionReads.ListAsync(new UserSubscriptionsByUserIdSpec(userId), cancellationToken))
            .FirstOrDefault(s => s.MaxioSubscriptionId == live.Id);

        if (existing is not null)
        {
            existing.UpdateFromBillingSystem(live.State, live.ProductPriceInCents,
                string.IsNullOrWhiteSpace(live.Currency) ? existing.Currency : live.Currency,
                live.CurrentPeriodEndsAt);
            await _subscriptions.UpdateAsync(existing, cancellationToken);
            await _subscriptions.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var handle = live.Product?.Handle ?? fallbackHandle;
        var record = new UserSubscription(
            userId: userId,
            maxioSubscriptionId: live.Id,
            maxioCustomerId: maxioCustomerId,
            productHandle: handle,
            productName: live.Product?.Name ?? handle,
            productPriceInCents: live.ProductPriceInCents,
            currency: live.Currency,
            state: live.State,
            nextBillingDateUtc: live.CurrentPeriodEndsAt);
        await _subscriptions.AddAsync(record, cancellationToken);
        await _subscriptions.SaveChangesAsync(cancellationToken);
        return record;
    }

    private string GetFamilyHandle() => _settings.ProductFamilyHandle;

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(SiteCurrencyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SiteCurrencyCacheLifetime;
            return await _maxio.GetSiteCurrencyAsync(cancellationToken);
        }) ?? "USD";
    }

    private static bool IsOpen(string state) => OpenStates.Contains(state);

    private static SubscriptionSummary ToSummary(UserSubscription record, bool created) => new()
    {
        SubscriptionId = record.MaxioSubscriptionId,
        PlanHandle = record.ProductHandle,
        PlanName = record.ProductName,
        Price = record.ProductPriceInCents / 100m,
        Currency = record.Currency,
        State = record.State,
        NextBillingDateUtc = record.NextBillingDateUtc,
        Created = created
    };

    private static string CustomerReferenceFor(string userId) => $"eshop-web:{userId}";

    private static string SubscriptionReferenceFor(string userId, string planHandle) =>
        $"eshop-web-sub:{userId}:{planHandle}";

    private static (string First, string Last) DeriveName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var tokens = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            return (Capitalize(tokens[0]), Capitalize(tokens[^1]));
        }
        return (Capitalize(localPart), "Subscriber");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}