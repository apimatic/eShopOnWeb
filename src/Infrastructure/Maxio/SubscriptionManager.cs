using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed record SubscriptionPlanInfo(
    string Handle,
    string Name,
    int ProductId,
    int PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequireCreditCard);

public sealed record SubscriptionResult(
    bool Created,
    int MaxioSubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string Currency,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? ActivatedAt,
    int MaxioCustomerId,
    string MaxioCustomerReference);

public sealed record UserSubscriptionInfo(
    int MaxioSubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string Currency,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? ActivatedAt,
    bool Stale);

/// <summary>
/// Orchestrates the eShopOnWeb subscribe flow on top of Maxio Advanced Billing:
/// ensures a Maxio customer exists for the ASP.NET Identity user (keyed by a
/// unique customer reference), creates subscriptions idempotently, and lists
/// the user's subscriptions hydrated with live Maxio state.
/// </summary>
public interface ISubscriptionManager
{
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct);

    Task<SubscriptionResult> SubscribeAsync(string userId, string email, string productHandle, CancellationToken ct);

    Task<IReadOnlyList<UserSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId, CancellationToken ct);
}

public sealed class SubscriptionManager : ISubscriptionManager
{
    private static readonly TimeSpan PlanCacheLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FamilyCacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "on_hold", "assessment", "pending", "soft_failure", "unpaid"
    };

    private static readonly TimeSpan UserLockTimeout = TimeSpan.FromSeconds(30);

    private readonly IMaxioGateway _gateway;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionManager> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _userPlanLocks = new();

    public SubscriptionManager(
        IMaxioGateway gateway,
        AppIdentityDbContext identityDbContext,
        IMemoryCache cache,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionManager> logger)
    {
        _gateway = gateway;
        _identityDbContext = identityDbContext;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct)
    {
        var plans = await GetPlansAsync(ct);
        return plans;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string email, string productHandle,
        CancellationToken ct)
    {
        var plans = await GetPlansAsync(ct);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioNotFoundException(
                $"Plan '{productHandle}' was not found in product family '{_options.ProductFamilyHandle}'.");
        }

        var lockKey = $"{userId}|{plan.Handle.ToLowerInvariant()}";
        var gate = _userPlanLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(UserLockTimeout, ct))
        {
            throw new MaxioConflictException(
                "A subscribe request for this plan is already in progress; retry in a moment.");
        }

        try
        {
            var customer = await EnsureCustomerAsync(userId, email, ct);

            var existing = await FindExistingSubscriptionAsync(customer.Id, userId, plan, ct);
            if (existing is not null)
            {
                return existing;
            }

            var subscription = await _gateway.CreateSubscriptionAsync(customer.Id, plan.ProductId, ct);

            await SaveLocalRecordAsync(userId, customer, subscription, ct);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({PlanHandle}) for user {UserId}",
                subscription.Id, plan.Handle, userId);

            return MapResult(created: true, subscription, plan.Handle);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId,
        CancellationToken ct)
    {
        var records = await _identityDbContext.AppUserSubscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);

        var result = new List<UserSubscriptionInfo>(records.Count);
        foreach (var record in records)
        {
            var stale = false;
            MaxioSubscription? live = null;
            try
            {
                live = await _gateway.GetSubscriptionAsync(record.MaxioSubscriptionId, ct);
            }
            catch (MaxioException ex)
            {
                _logger.LogWarning(ex,
                    "Could not refresh Maxio subscription {SubscriptionId}; falling back to local state.",
                    record.MaxioSubscriptionId);
                stale = true;
            }

            if (live is not null && !string.Equals(live.State, record.State, StringComparison.OrdinalIgnoreCase))
            {
                record.UpdateState(live.State);
                await _identityDbContext.SaveChangesAsync(ct);
            }

            result.Add(new UserSubscriptionInfo(
                MaxioSubscriptionId: record.MaxioSubscriptionId,
                State: live?.State ?? record.State,
                PlanHandle: live?.Product?.Handle ?? record.ProductHandle,
                PlanName: live?.Product?.Name ?? record.ProductName,
                PriceInCents: live?.Product?.PriceInCents ?? record.PriceInCents,
                Currency: live?.Currency ?? record.Currency,
                NextBillingDate: IsUsable(live?.State ?? record.State) ? live?.CurrentPeriodEndsAt : null,
                ActivatedAt: live?.ActivatedAt,
                Stale: stale));
        }

        return result;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var customer = await _gateway.FindCustomerByReferenceAsync(userId, ct);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitEmailLocalPart(email);

        try
        {
            return await _gateway.CreateCustomerAsync(userId, firstName, lastName, email, ct);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 &&
            ex.Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase)))
        {
            // A concurrent request created the customer after our lookup; the
            // unique-reference constraint guarantees exactly one winner.
            var raced = await _gateway.FindCustomerByReferenceAsync(userId, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<SubscriptionResult?> FindExistingSubscriptionAsync(int maxioCustomerId, string userId,
        SubscriptionPlanInfo plan, CancellationToken ct)
    {
        var localRecord = await _identityDbContext.AppUserSubscriptions
            .Where(s => s.UserId == userId && s.ProductHandle == plan.Handle && ActiveStates.Contains(s.State))
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (localRecord is not null)
        {
            var live = await _gateway.GetSubscriptionAsync(localRecord.MaxioSubscriptionId, ct);
            if (live is not null && IsUsable(live.State))
            {
                if (!string.Equals(live.State, localRecord.State, StringComparison.OrdinalIgnoreCase))
                {
                    localRecord.UpdateState(live.State);
                    await _identityDbContext.SaveChangesAsync(ct);
                }

                _logger.LogInformation(
                    "Subscribe is idempotent: user {UserId} already has subscription {SubscriptionId} for plan {PlanHandle}.",
                    userId, live.Id, plan.Handle);
                return MapResult(created: false, live, plan.Handle);
            }
        }

        // Guard against a lost local store (e.g. in-memory database restart):
        // ask Maxio whether this customer already holds a usable subscription
        // to the same product before creating another one.
        var customerSubscriptions = await _gateway.ListSubscriptionsForCustomerAsync(maxioCustomerId, ct);
        var remoteMatch = customerSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            IsUsable(s.State));

        if (remoteMatch is not null)
        {
            await SaveLocalRecordAsync(userId, maxioCustomerId, userId, remoteMatch, ct);
            _logger.LogInformation(
                "Subscribe recovered an existing Maxio subscription {SubscriptionId} for user {UserId}.",
                remoteMatch.Id, userId);
            return MapResult(created: false, remoteMatch, plan.Handle);
        }

        return null;
    }

    private async Task<AppUserSubscription> SaveLocalRecordAsync(string userId, MaxioCustomer customer,
        MaxioSubscription subscription, CancellationToken ct)
    {
        return await SaveLocalRecordAsync(userId, customer.Id, customer.Reference ?? userId, subscription, ct);
    }

    private async Task<AppUserSubscription> SaveLocalRecordAsync(string userId, int maxioCustomerId,
        string maxioCustomerReference, MaxioSubscription subscription, CancellationToken ct)
    {
        var existing = await _identityDbContext.AppUserSubscriptions
            .FirstOrDefaultAsync(s => s.MaxioSubscriptionId == subscription.Id, ct);
        if (existing is not null)
        {
            return existing;
        }

        var record = new AppUserSubscription(
            userId: userId,
            maxioCustomerId: maxioCustomerId,
            maxioCustomerReference: maxioCustomerReference,
            maxioSubscriptionId: subscription.Id,
            productHandle: subscription.Product?.Handle ?? string.Empty,
            productName: subscription.Product?.Name ?? string.Empty,
            priceInCents: subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            currency: subscription.Currency ?? "USD",
            state: subscription.State);

        _identityDbContext.AppUserSubscriptions.Add(record);
        await _identityDbContext.SaveChangesAsync(ct);
        return record;
    }

    private SubscriptionResult MapResult(bool created, MaxioSubscription subscription, string planHandle) =>
        new(
            Created: created,
            MaxioSubscriptionId: subscription.Id,
            State: subscription.State,
            PlanHandle: subscription.Product?.Handle ?? planHandle,
            PlanName: subscription.Product?.Name ?? string.Empty,
            PriceInCents: subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents ?? 0,
            Currency: subscription.Currency ?? "USD",
            NextBillingDate: IsUsable(subscription.State) ? subscription.CurrentPeriodEndsAt : null,
            ActivatedAt: subscription.ActivatedAt,
            MaxioCustomerId: subscription.Customer?.Id ?? 0,
            MaxioCustomerReference: subscription.Customer?.Reference ?? string.Empty);

    private async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken ct)
    {
        var family = await ResolveFamilyAsync(ct);
        if (family is null)
        {
            throw new MaxioNotFoundException(
                $"Product family '{_options.ProductFamilyHandle}' was not found in the Maxio site.");
        }

        return await _cache.GetOrCreateAsync($"maxio-plans:{family.Id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlanCacheLifetime;
            var products = await _gateway.ListProductsInFamilyAsync(family.Id, ct);
            return products
                .Select(p => new SubscriptionPlanInfo(
                    Handle: p.Handle,
                    Name: p.Name,
                    ProductId: p.Id,
                    PriceInCents: p.PriceInCents,
                    Interval: p.Interval,
                    IntervalUnit: p.IntervalUnit,
                    RequireCreditCard: p.RequireCreditCard))
                .ToList();
        }) ?? new List<SubscriptionPlanInfo>();
    }

    private async Task<MaxioProductFamily?> ResolveFamilyAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync($"maxio-family:{_options.ProductFamilyHandle}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FamilyCacheLifetime;
            return await _gateway.FindProductFamilyByHandleAsync(_options.ProductFamilyHandle, ct);
        });
    }

    private static bool IsUsable(string? state) => state is not null && ActiveStates.Contains(state);

    private static (string FirstName, string LastName) SplitEmailLocalPart(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(parts.FirstOrDefault() ?? "eShop");
        var lastName = Capitalize(parts.Skip(1).FirstOrDefault() ?? "Customer");
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
