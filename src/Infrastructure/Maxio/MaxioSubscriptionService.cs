using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing against Maxio Advanced Billing. The caller's user name is the Maxio customer reference.
/// Subscribe order: validate plan -> claim (local primary key) -> SDK calls -> record result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 10;
    private const int SubscriptionListCap = 200;
    private const string CustomerLastName = "(eShopOnWeb)";
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        CatalogContext db,
        UserManager<ApplicationUser> userManager,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var cts = Bounded(cancellationToken, ReadBudget);
        try
        {
            return await ReadPlansAsync(cts.Token);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "list plans");
        }
    }

    public async Task<SubscriptionList> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        using var cts = Bounded(cancellationToken, ReadBudget);
        try
        {
            // Answered by the provider, never by local rows: a new user, a restart or a reset store all still work.
            var customerId = await FindCustomerIdAsync(userName, cts.Token);
            if (customerId is null)
            {
                return new SubscriptionList(Array.Empty<SubscriptionSummary>(), false);
            }

            var items = await ReadSubscriptionsAsync(customerId.Value, cts.Token);
            return new SubscriptionList(items, items.Count >= SubscriptionListCap);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "list subscriptions");
        }
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        using var cts = Bounded(cancellationToken, SubscribeBudget);
        var ct = cts.Token;

        // 1. The plan must be one this family offers (not merely one that exists at the provider).
        SubscriptionPlanList plans;
        try
        {
            plans = await ReadPlansAsync(ct);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "list plans");
        }

        if (plans.Plans.All(p => p.Handle != planHandle))
        {
            if (plans.IsTruncated)
            {
                throw new SubscriptionException(SubscriptionFailure.Unavailable, "The plan list could not be read completely; try again later.");
            }

            _logger.LogWarning("Subscribe rejected: unknown plan {PlanHandle} for {User}", planHandle, userName);
            throw new SubscriptionException(SubscriptionFailure.Invalid, $"'{planHandle}' is not an available subscription plan.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionException(SubscriptionFailure.Invalid, "The account has no email address; one is required to subscribe.");
        }

        // 2. Claim before any provider call. The store refuses a second claim for (user, plan).
        SubscriptionSummary? existing;
        try
        {
            existing = await ClaimAsync(userName, planHandle, ct);
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "check existing subscriptions");
        }

        if (existing is not null)
        {
            _logger.LogInformation("Subscribe for {User} on {PlanHandle} returned existing subscription {SubscriptionId}", userName, planHandle, existing.SubscriptionId);
            return existing;
        }

        // 3. Customer (idempotent by reference), then the subscription.
        int customerId;
        try
        {
            customerId = await EnsureCustomerAsync(userName, user.Email!, ct);

            // The claim store may have been reset (restart, in-memory store): the provider is the record.
            var alreadySubscribed = await FindLiveInCustomerAsync(customerId, planHandle, ct);
            if (alreadySubscribed is not null)
            {
                await CompleteClaimAsync(userName, planHandle, customerId, alreadySubscribed.SubscriptionId);
                return alreadySubscribed;
            }
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException || ex is SubscriptionException)
        {
            // Nothing subscription-shaped reached the provider: release so the user can retry.
            await ReleaseClaimAsync(userName, planHandle);
            if (ex is SdkException sdk)
            {
                throw Translate(sdk, "ensure customer");
            }

            throw;
        }

        SubscriptionSummary summary;
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = planHandle,
                            // No card is captured by this app, so bill by invoice (remittance) instead of automatic card collection.
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                            CustomerId = customerId
                        }
                    }
                },
                cancellationToken: ct);
            summary = ToSummary(response.Subscription, planHandle)
                ?? throw new InvalidOperationException("unreadable subscription");
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(userName, planHandle);
            throw Translate(ex, "create subscription");
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
            when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // Definitive refusal by the provider: release the claim and log the provider's validation messages.
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                _logger.LogWarning("Maxio rejected subscription for {User} on {PlanHandle}: {Errors}", userName, planHandle, string.Join("; ", errorList.Errors));
            }

            await ReleaseClaimAsync(userName, planHandle);
            throw Translate(ex, "create subscription");
        }
        catch (ApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            // Definitive refusal by the provider: release the claim.
            await ReleaseClaimAsync(userName, planHandle);
            throw Translate(ex, "create subscription");
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException || ex is InvalidOperationException)
        {
            // Unknown outcome (timeout, dropped connection, 5xx, unreadable body): the write may have landed.
            // Settle it by re-reading the customer's subscriptions; otherwise keep the claim as 'pending'.
            var found = await TrySettleAsync(customerId, planHandle);
            if (found is null)
            {
                _logger.LogWarning("Subscribe outcome unknown for {User} on {PlanHandle}; claim kept pending", userName, planHandle);
                throw new SubscriptionException(SubscriptionFailure.Pending,
                    "The subscription request may not have completed. Check your subscriptions shortly.", ex);
            }

            summary = found;
        }

        await CompleteClaimAsync(userName, planHandle, customerId, summary.SubscriptionId);
        _logger.LogInformation("Subscribed {User} to {PlanHandle} as subscription {SubscriptionId}", userName, planHandle, summary.SubscriptionId);
        return summary;
    }

    // ---- claim handling ------------------------------------------------------------------

    /// <summary>Returns null when the claim was taken; otherwise the live subscription already held for this plan.</summary>
    private async Task<SubscriptionSummary?> ClaimAsync(string userName, string planHandle, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                _db.SubscriptionClaims.Add(new SubscriptionClaim { UserName = userName, PlanHandle = planHandle });
                await _db.SaveChangesAsync(ct);
                return null;
            }
            catch (Exception ex) when (ex is DbUpdateException || ex is ArgumentException)
            {
                _db.ChangeTracker.Clear();
            }

            var claim = await _db.SubscriptionClaims.AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserName == userName && c.PlanHandle == planHandle, ct);
            if (claim is null)
            {
                continue; // released in the meantime
            }

            var live = await FindLiveSubscriptionAsync(userName, planHandle, ct);
            if (live is not null)
            {
                if (claim.Status != SubscriptionClaimStatus.Completed)
                {
                    await CompleteClaimAsync(userName, planHandle, null, live.SubscriptionId);
                }

                return live;
            }

            var stale = DateTimeOffset.UtcNow - claim.CreatedAt > StaleClaimAfter;
            if (claim.Status == SubscriptionClaimStatus.Completed || stale)
            {
                // Completed but no longer live (cancelled), or abandoned: free the key and claim again.
                await ReleaseClaimAsync(userName, planHandle);
                continue;
            }

            _logger.LogWarning("Subscribe conflict for {User} on {PlanHandle}: another request is in progress", userName, planHandle);
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request for this plan is already in progress.");
        }

        throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request for this plan is already in progress.");
    }

    private async Task ReleaseClaimAsync(string userName, string planHandle)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var claim = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserName == userName && c.PlanHandle == planHandle);
            if (claim is not null)
            {
                _db.SubscriptionClaims.Remove(claim);
                await _db.SaveChangesAsync();
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning("Could not release subscription claim for {User}/{PlanHandle}: {Error}", userName, planHandle, ex.Message);
            _db.ChangeTracker.Clear();
        }
    }

    private async Task CompleteClaimAsync(string userName, string planHandle, int? customerId, int subscriptionId)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var claim = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserName == userName && c.PlanHandle == planHandle);
            if (claim is null)
            {
                return;
            }

            claim.Status = SubscriptionClaimStatus.Completed;
            claim.MaxioSubscriptionId = subscriptionId;
            claim.MaxioCustomerId = customerId ?? claim.MaxioCustomerId;
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // The provider is the record; the claim row is only a guard.
            _logger.LogWarning("Could not complete subscription claim for {User}/{PlanHandle}: {Error}", userName, planHandle, ex.Message);
            _db.ChangeTracker.Clear();
        }
    }

    // ---- provider calls -----------------------------------------------------------------

    private async Task<SubscriptionPlanList> ReadPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var items = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                    Page = page,
                    PerPage = PlanPageSize
                },
                cancellationToken: ct);

            foreach (var product in items.Select(i => i.Product))
            {
                if (string.IsNullOrEmpty(product.Handle) || product.ArchivedAt is not null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.Description,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (items.Count < PlanPageSize)
            {
                break;
            }

            truncated = page == MaxPlanPages;
        }

        return new SubscriptionPlanList(plans, truncated);
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer.Id
                ?? throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider returned an unreadable customer.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // provider-confirmed: no such customer
        }
    }

    private async Task<int> EnsureCustomerAsync(string userName, string email, CancellationToken ct)
    {
        var existing = await FindCustomerIdAsync(userName, ct);
        if (existing is not null)
        {
            return existing.Value;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = userName,
                            LastName = CustomerLastName,
                            Email = email,
                            Reference = userName
                        }
                    }
                },
                cancellationToken: ct);
            return response.Customer.Id
                ?? throw new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider returned an unreadable customer.");
        }
        catch (Exception ex) when (ex is ApiException or SdkConnectionException)
        {
            // A racing request or a lost response may already have created the customer (reference is unique).
            var settled = await FindCustomerIdAsync(userName, ct);
            if (settled is not null)
            {
                return settled.Value;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<SubscriptionSummary>> ReadSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var items = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        return items
            .Select(i => ToSummary(i.Subscription, null))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    private async Task<SubscriptionSummary?> FindLiveSubscriptionAsync(string userName, string planHandle, CancellationToken ct)
    {
        var customerId = await FindCustomerIdAsync(userName, ct);
        return customerId is null ? null : await FindLiveInCustomerAsync(customerId.Value, planHandle, ct);
    }

    private async Task<SubscriptionSummary?> FindLiveInCustomerAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subs = await ReadSubscriptionsAsync(customerId, ct);
        return subs.FirstOrDefault(s => s.PlanHandle == planHandle && IsLive(s.State));
    }

    /// <summary>Re-reads provider state after an ambiguous write. Never throws; null means still unknown.</summary>
    private async Task<SubscriptionSummary?> TrySettleAsync(int customerId, string planHandle)
    {
        try
        {
            using var cts = new CancellationTokenSource(ReadBudget);
            return await FindLiveInCustomerAsync(customerId, planHandle, cts.Token);
        }
        catch (Exception ex) when (ex is SdkException || ex is OperationCanceledException)
        {
            return null;
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    private static bool IsLive(string state) =>
        state != SubscriptionState.Canceled.Value
        && state != SubscriptionState.Expired.Value
        && state != SubscriptionState.FailedToCreate.Value;

    private static SubscriptionSummary? ToSummary(MaxioAdvancedBilling.Models.Subscription? s, string? fallbackPlanHandle)
    {
        if (s?.Id is null)
        {
            return null;
        }

        var handle = s.Product?.Handle ?? fallbackPlanHandle;
        if (handle is null)
        {
            return null;
        }

        return new SubscriptionSummary(
            s.Id.Value,
            handle,
            s.Product?.Name ?? handle,
            s.ProductPriceInCents ?? s.CurrentBillingAmountInCents ?? 0,
            s.State?.Value ?? "unknown",
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);
    }

    private static CancellationTokenSource Bounded(CancellationToken ct, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        return cts;
    }

    /// <summary>Single mapping from provider failures to caller-safe failures; SDK text never reaches callers.</summary>
    private SubscriptionException Translate(SdkException ex, string operation)
    {
        _logger.LogWarning("Maxio call failed during {Operation}: {Error}", operation, ex.Message);
        if (ex is ApiException api)
        {
            var status = (int)api.StatusCode;
            // 401/403/429 are our credentials/quota, not the caller's fault.
            if (status is >= 400 and < 500 && status is not (401 or 403 or 429))
            {
                return new SubscriptionException(SubscriptionFailure.Invalid, "The billing provider rejected the request.", ex);
            }
        }

        return new SubscriptionException(SubscriptionFailure.Unavailable, "The billing provider is unavailable.", ex);
    }
}
