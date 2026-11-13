using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioProduct = MaxioAdvancedBilling.Models.Product;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ISubscriptionBillingService
{
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

/// <summary>
/// The Subscribe flow with Maxio as the billing system of record.
/// <para>
/// Duplicate prevention: each of the two writes (create customer, create subscription) is guarded by a
/// <see cref="BillingClaim"/> whose primary key is the write's deterministic Maxio reference. Order is always
/// claim -> Maxio call -> record the result; a second caller is refused by the store before it reaches Maxio.
/// Before every create the service also re-reads Maxio by that reference, so a claim lost on restart cannot
/// cause a duplicate either.
/// </para>
/// <para>
/// Unknown outcomes: a create whose connection fails after sending is settled in its own catch by re-reading
/// Maxio by reference; if that is inconclusive the claim is recorded as Unknown and settled by the next call.
/// </para>
/// </summary>
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    // Maxio's maximum page size for product lists.
    internal const int PlanPageSize = 200;

    private readonly IMaxioBillingGateway _gateway;
    private readonly IBillingClaimStore _claims;
    private readonly IMemoryCache _cache;
    private readonly MaxioSettings _settings;
    private readonly TimeProvider _time;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        IBillingClaimStore claims,
        IMemoryCache cache,
        IOptions<MaxioSettings> settings,
        TimeProvider time,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _claims = claims;
        _cache = cache;
        _settings = settings.Value;
        _time = time;
        _userManager = userManager;
        _logger = logger;
    }

    private string ProductFamilyHandle => _settings.ProductFamilyHandle!.Trim();

    private TimeSpan UnknownOutcomeWindow => TimeSpan.FromSeconds(_settings.UnknownOutcomeWindowSeconds);

    // A Pending claim older than this was abandoned by a caller that died mid-call; anything it sent is past the
    // window in which Maxio could still complete it.
    private TimeSpan PendingLease => TimeSpan.FromSeconds(_settings.RequestBudgetSeconds) + UnknownOutcomeWindow;

    // Budget for settling an unknown outcome; deliberately NOT linked to the caller, who may have gone away.
    private TimeSpan SettleBudget => TimeSpan.FromSeconds(_settings.AttemptTimeoutSeconds + 5);

    // ------------------------------------------------------------------ plans

    public Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
        WithinBudget(cancellationToken, GetPlansCoreAsync);

    private async Task<PlanCatalog> GetPlansCoreAsync(CancellationToken token)
    {
        var family = ProductFamilyHandle;
        var cacheKey = "maxio:plans:" + family;
        if (_settings.PlanCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out PlanCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var plans = new List<PlanView>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            IReadOnlyList<MaxioProduct> products;
            try
            {
                products = await _gateway.ListProductsPageAsync(family, page, PlanPageSize, token);
            }
            catch (MaxioGatewayException ex)
            {
                throw BillingException.From(ex);
            }

            plans.AddRange(products
                .Where(p => !string.IsNullOrWhiteSpace(p.Handle) && p.ArchivedAt is null)
                .Select(p => new PlanView(p.Handle!, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value)));

            if (products.Count < PlanPageSize)
            {
                break;
            }
            if (page >= _settings.PlanPageCap)
            {
                // The result is partial: say so in the result itself, not only in the log.
                truncated = true;
                _logger.LogWarning("Plan list for product family {Family} stopped at the {PageCap}-page cap and may be incomplete", family, _settings.PlanPageCap);
                break;
            }
        }

        var catalog = new PlanCatalog(plans, truncated);
        if (!truncated && _settings.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, catalog, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        }
        return catalog;
    }

    // ------------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "The caller's identity is missing from the token.");
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "planHandle is required.");
        }
        return WithinBudget(cancellationToken, token => SubscribeCoreAsync(userName.Trim(), planHandle.Trim(), token, retryStaleRecord: true));
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(string userName, string planHandle, CancellationToken token, bool retryStaleRecord)
    {
        // Invariant: only a plan this application offers (the plan list) may be subscribed to.
        var catalog = await GetPlansCoreAsync(token);
        if (!catalog.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
        {
            if (catalog.Truncated)
            {
                throw new BillingException(BillingErrorKind.Unavailable, "The plan list could not be read completely; please retry shortly.");
            }
            throw new BillingException(BillingErrorKind.InvalidRequest,
                $"Unknown plan '{planHandle}'. GET /api/subscription-plans lists the plans available.");
        }

        var reference = BillingReferences.Subscription(userName, ProductFamilyHandle);

        // 1. Claim (the store refuses a second caller here, before anything reaches Maxio).
        var (held, done) = await AcquireClaimAsync(reference, BillingClaimKind.Subscription, planHandle,
            async t => (await _gateway.FindSubscriptionByReferenceAsync(reference, t))?.Id?.ToString(CultureInfo.InvariantCulture),
            token);

        if (done is not null)
        {
            return await ExistingSubscriptionAsync(done, userName, planHandle, token, retryStaleRecord);
        }

        // 2. Reconcile and ensure the customer. Nothing has been written to Maxio for this claim yet, so any
        //    failure here releases it.
        MaxioSubscription? alreadyThere;
        int customerId = 0;
        try
        {
            alreadyThere = await _gateway.FindSubscriptionByReferenceAsync(reference, token);
            if (alreadyThere is null)
            {
                customerId = await EnsureCustomerAsync(userName, token);
            }
        }
        catch (Exception ex)
        {
            await ReleaseQuietlyAsync(held!);
            if (ex is MaxioGatewayException gateway)
            {
                throw BillingException.From(gateway);
            }
            throw;
        }

        if (alreadyThere is not null)
        {
            await CompleteAsync(held!, alreadyThere, planHandle);
            return Existing(alreadyThere, planHandle);
        }

        // 3. The write.
        MaxioSubscription created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(planHandle, customerId, reference, token);
        }
        catch (MaxioGatewayException ex) when (!ex.OutcomeUnknown)
        {
            // Maxio definitively did not act: release so the shopper can correct and retry.
            await ReleaseQuietlyAsync(held!);
            throw BillingException.From(ex);
        }
        catch (Exception ex) when (ex is MaxioGatewayException or OperationCanceledException)
        {
            // The request may have reached Maxio: settle by reading it back, never report a plain failure.
            return await SettleSubscriptionCreateAsync(held!, reference, planHandle, ex);
        }

        // 4. Record the result.
        await CompleteAsync(held!, created, planHandle);
        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} ({SubscriptionReference}) for customer {CustomerId} on plan {PlanHandle}, state {State}",
            created.Id, reference, customerId, planHandle, created.State?.Value);
        return new SubscribeResult(SubscribeStatus.Created, ToView(created));
    }

    private async Task<SubscribeResult> SettleSubscriptionCreateAsync(BillingClaim held, string reference, string planHandle, Exception failure)
    {
        _logger.LogError(failure, "CreateSubscription outcome unknown for {SubscriptionReference}; re-reading Maxio by reference", reference);
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            var found = await _gateway.FindSubscriptionByReferenceAsync(reference, settle.Token);
            if (found is not null)
            {
                await CompleteAsync(held, found, planHandle);
                _logger.LogInformation("Unknown CreateSubscription outcome settled: subscription {SubscriptionId} exists for {SubscriptionReference}", found.Id, reference);
                return new SubscribeResult(SubscribeStatus.Created, ToView(found));
            }
        }
        catch (Exception ex) when (ex is MaxioGatewayException or OperationCanceledException)
        {
            _logger.LogError(ex, "Re-reading subscription {SubscriptionReference} after an unknown outcome failed", reference);
        }

        // Not visible (yet): Maxio may still complete the request. Record Unknown; the next subscribe or
        // my-subscriptions call settles it, and nothing re-creates it inside the window.
        await MarkUnknownAsync(held);
        return new SubscribeResult(SubscribeStatus.PendingConfirmation, null);
    }

    private async Task<SubscribeResult> ExistingSubscriptionAsync(BillingClaim done, string userName, string planHandle, CancellationToken token, bool retryStaleRecord)
    {
        MaxioSubscription? subscription;
        try
        {
            subscription = await _gateway.FindSubscriptionByReferenceAsync(done.Key, token);
        }
        catch (MaxioGatewayException ex)
        {
            throw BillingException.From(ex);
        }

        if (subscription is null)
        {
            // The local record points at a subscription Maxio no longer has (e.g. the site was re-seeded).
            // Drop the stale record and go through the flow once more.
            _logger.LogWarning("Recorded subscription {SubscriptionReference} is not in Maxio; discarding the stale record", done.Key);
            if (retryStaleRecord && await _claims.TryReleaseAsync(done, token))
            {
                return await SubscribeCoreAsync(userName, planHandle, token, retryStaleRecord: false);
            }
            throw new BillingException(BillingErrorKind.Conflict, "A subscription request for this account is already being processed; please retry shortly.");
        }
        return Existing(subscription, planHandle, done.PlanHandle);
    }

    private static SubscribeResult Existing(MaxioSubscription subscription, string requestedPlan, string? recordedPlan = null)
    {
        var view = ToView(subscription);
        var currentPlan = view.PlanHandle ?? recordedPlan;
        if (!string.Equals(currentPlan, requestedPlan, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict,
                $"This account already has a subscription to plan '{currentPlan}' (state: {view.State}). Changing plans is not supported here.");
        }
        if (view.State is "canceled" or "expired")
        {
            throw new BillingException(BillingErrorKind.Conflict,
                $"This account's subscription to plan '{currentPlan}' is {view.State}. Reactivation is not supported here.");
        }
        return new SubscribeResult(SubscribeStatus.AlreadySubscribed, view);
    }

    // ------------------------------------------------------------------ customer

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken token)
    {
        var reference = BillingReferences.Customer(userName);
        var (held, done) = await AcquireClaimAsync(reference, BillingClaimKind.Customer, null,
            async t => (await _gateway.FindCustomerByReferenceAsync(reference, t))?.Id?.ToString(CultureInfo.InvariantCulture),
            token);

        if (done is not null)
        {
            return int.Parse(done.ExternalId!, CultureInfo.InvariantCulture);
        }

        MaxioAdvancedBilling.Models.Customer? existing;
        (string Email, string FirstName, string LastName)? contact = null;
        try
        {
            // Reconcile first: the customer may exist already (claim lost on restart, earlier unknown outcome).
            existing = await _gateway.FindCustomerByReferenceAsync(reference, token);
            if (existing is null)
            {
                contact = await ResolveContactAsync(userName);
            }
        }
        catch
        {
            await ReleaseQuietlyAsync(held!);
            throw;
        }

        if (existing is not null)
        {
            await CompleteCustomerAsync(held!, existing.Id!.Value);
            return existing.Id.Value;
        }

        MaxioAdvancedBilling.Models.Customer created;
        try
        {
            var (email, firstName, lastName) = contact!.Value;
            created = await _gateway.CreateCustomerAsync(reference, email, firstName, lastName, token);
        }
        catch (MaxioGatewayException ex) when (!ex.OutcomeUnknown)
        {
            await ReleaseQuietlyAsync(held!);
            throw;
        }
        catch (Exception ex) when (ex is MaxioGatewayException or OperationCanceledException)
        {
            var settledId = await SettleCustomerCreateAsync(held!, reference, ex);
            if (settledId is not null)
            {
                return settledId.Value;
            }
            throw new BillingException(BillingErrorKind.Unavailable,
                "Your billing account could not be confirmed yet. Nothing was subscribed or charged; please retry in a few minutes.");
        }

        await CompleteCustomerAsync(held!, created.Id!.Value);
        _logger.LogInformation("Created Maxio customer {CustomerId} ({CustomerReference})", created.Id, reference);
        return created.Id.Value;
    }

    private async Task<int?> SettleCustomerCreateAsync(BillingClaim held, string reference, Exception failure)
    {
        _logger.LogError(failure, "CreateCustomer outcome unknown for {CustomerReference}; re-reading Maxio by reference", reference);
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            var found = await _gateway.FindCustomerByReferenceAsync(reference, settle.Token);
            if (found is not null)
            {
                await CompleteCustomerAsync(held, found.Id!.Value);
                return found.Id.Value;
            }
        }
        catch (Exception ex) when (ex is MaxioGatewayException or OperationCanceledException)
        {
            _logger.LogError(ex, "Re-reading customer {CustomerReference} after an unknown outcome failed", reference);
        }
        await MarkUnknownAsync(held);
        return null;
    }

    private async Task<(string Email, string FirstName, string LastName)> ResolveContactAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName;
        if (!MailAddress.TryCreate(email, out var address))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "Your account has no valid e-mail address, which billing requires.");
        }

        // eShopOnWeb accounts carry no personal names; Maxio requires both, so derive stable non-empty values.
        var firstName = address.User.Length > 50 ? address.User[..50] : address.User;
        return (address.Address, string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName, "Customer");
    }

    // ------------------------------------------------------------------ my subscriptions

    public Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "The caller's identity is missing from the token.");
        }
        return WithinBudget(cancellationToken, token => GetSubscriptionsCoreAsync(userName.Trim(), token));
    }

    private async Task<IReadOnlyList<SubscriptionView>> GetSubscriptionsCoreAsync(string userName, CancellationToken token)
    {
        await SettleUnknownSubscriptionAsync(BillingReferences.Subscription(userName, ProductFamilyHandle), token);

        try
        {
            var customerReference = BillingReferences.Customer(userName);
            var recorded = await _claims.FindAsync(customerReference, token);
            int? customerId = recorded is { State: BillingClaimState.Done, ExternalId: { } id }
                ? int.Parse(id, CultureInfo.InvariantCulture)
                : null;

            customerId ??= (await _gateway.FindCustomerByReferenceAsync(customerReference, token))?.Id;
            if (customerId is null)
            {
                return Array.Empty<SubscriptionView>();
            }

            IReadOnlyList<MaxioSubscription> subscriptions;
            try
            {
                subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, token);
            }
            catch (MaxioGatewayException ex) when (ex.Kind == MaxioFailureKind.NotFound && recorded is not null)
            {
                // The recorded customer id is stale (site re-seeded): fall back to the reference lookup.
                var current = (await _gateway.FindCustomerByReferenceAsync(customerReference, token))?.Id;
                if (current is null)
                {
                    return Array.Empty<SubscriptionView>();
                }
                subscriptions = await _gateway.ListCustomerSubscriptionsAsync(current.Value, token);
            }
            return subscriptions.Select(ToView).ToList();
        }
        catch (MaxioGatewayException ex)
        {
            throw BillingException.From(ex);
        }
    }

    /// <summary>The sweep for a subscription claim left Unknown: find it in Maxio, or release it once the window has passed.</summary>
    private async Task SettleUnknownSubscriptionAsync(string reference, CancellationToken token)
    {
        var claim = await _claims.FindAsync(reference, token);
        if (claim is not { State: BillingClaimState.Unknown })
        {
            return;
        }
        try
        {
            var found = await _gateway.FindSubscriptionByReferenceAsync(reference, token);
            var now = _time.GetUtcNow();
            if (found is not null)
            {
                await _claims.TryUpdateAsync(claim, c => c.Complete(found.Id!.Value.ToString(CultureInfo.InvariantCulture), found.Product?.Handle, now), token);
            }
            else if (now - claim.UpdatedAt >= UnknownOutcomeWindow)
            {
                await _claims.TryReleaseAsync(claim, token);
            }
        }
        catch (MaxioGatewayException ex)
        {
            // Listing still works without settling; the next call retries the sweep.
            _logger.LogWarning(ex, "Could not settle unknown subscription outcome {SubscriptionReference}", reference);
        }
    }

    // ------------------------------------------------------------------ claims

    /// <summary>
    /// Takes the claim for <paramref name="key"/>. Returns it as <c>held</c> when this caller may perform the write,
    /// or as <c>done</c> when the write already happened. Throws a 409 when another caller holds it.
    /// </summary>
    private async Task<(BillingClaim? Held, BillingClaim? Done)> AcquireClaimAsync(
        string key, BillingClaimKind kind, string? planHandle,
        Func<CancellationToken, Task<string?>> findExternalId, CancellationToken token)
    {
        var now = _time.GetUtcNow();
        var fresh = new BillingClaim(key, kind, planHandle, now);
        if (await _claims.TryInsertAsync(fresh, token))
        {
            return (fresh, null);
        }

        var existing = await _claims.FindAsync(key, token);
        if (existing is null)
        {
            // Released between our insert and our read: one more attempt, then give up.
            fresh = new BillingClaim(key, kind, planHandle, now);
            if (await _claims.TryInsertAsync(fresh, token))
            {
                return (fresh, null);
            }
            throw InProgress();
        }

        switch (existing.State)
        {
            case BillingClaimState.Done:
                return (null, existing);

            case BillingClaimState.Pending when !existing.IsStale(now, PendingLease):
                throw InProgress();

            case BillingClaimState.Unknown:
                string? externalId;
                try
                {
                    externalId = await findExternalId(token);
                }
                catch (MaxioGatewayException ex)
                {
                    throw BillingException.From(ex);
                }
                if (externalId is not null)
                {
                    if (await _claims.TryUpdateAsync(existing, c => c.Complete(externalId, null, now), token))
                    {
                        return (null, existing);
                    }
                    throw InProgress();
                }
                if (now - existing.UpdatedAt < UnknownOutcomeWindow)
                {
                    // Maxio may still be completing the earlier request: creating again now could duplicate it.
                    throw new BillingException(BillingErrorKind.Conflict,
                        "An earlier subscription request for this account is still being confirmed; please retry in a few minutes.");
                }
                break;
        }

        // Unknown past its window, or a Pending claim abandoned by a dead caller: take it over atomically.
        if (!await _claims.TryUpdateAsync(existing, c => c.Begin(planHandle, now), token))
        {
            throw InProgress();
        }
        return (existing, null);
    }

    private static BillingException InProgress() =>
        new(BillingErrorKind.Conflict, "A subscription request for this account is already in progress; please retry shortly.");

    private Task CompleteAsync(BillingClaim claim, MaxioSubscription subscription, string planHandle) =>
        UpdateClaimAsync(claim, c => c.Complete(subscription.Id!.Value.ToString(CultureInfo.InvariantCulture), subscription.Product?.Handle ?? planHandle, _time.GetUtcNow()));

    private Task CompleteCustomerAsync(BillingClaim claim, int customerId) =>
        UpdateClaimAsync(claim, c => c.Complete(customerId.ToString(CultureInfo.InvariantCulture), null, _time.GetUtcNow()));

    private Task MarkUnknownAsync(BillingClaim claim) =>
        UpdateClaimAsync(claim, c => c.MarkUnknown(_time.GetUtcNow()));

    private async Task UpdateClaimAsync(BillingClaim claim, Action<BillingClaim> change)
    {
        // Recording an outcome must not be cut short by the caller going away.
        using var cts = new CancellationTokenSource(SettleBudget);
        if (!await _claims.TryUpdateAsync(claim, change, cts.Token))
        {
            _logger.LogWarning("Billing claim {ClaimKey} was changed by another caller before its outcome could be recorded", claim.Key);
        }
    }

    private async Task ReleaseQuietlyAsync(BillingClaim claim)
    {
        try
        {
            using var cts = new CancellationTokenSource(SettleBudget);
            await _claims.TryReleaseAsync(claim, cts.Token);
        }
        catch (Exception ex)
        {
            // A claim that cannot be released expires through the Pending lease.
            _logger.LogError(ex, "Could not release billing claim {ClaimKey}", claim.Key);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs <paramref name="work"/> under the per-request budget, linked to the caller's cancellation.</summary>
    private async Task<T> WithinBudget<T>(CancellationToken cancellationToken, Func<CancellationToken, Task<T>> work)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_settings.RequestBudgetSeconds));
        try
        {
            return await work(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingException(BillingErrorKind.Unavailable, "Billing did not respond in time. Please retry shortly.", null, ex);
        }
    }

    internal static SubscriptionView ToView(MaxioSubscription s) => new(
        s.Id!.Value,
        s.Reference,
        s.State?.Value ?? "unknown",
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents,
        s.Currency,
        // "Next billing date": when Maxio will next assess the subscription; it usually tracks the period end.
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CurrentPeriodEndsAt);
}
