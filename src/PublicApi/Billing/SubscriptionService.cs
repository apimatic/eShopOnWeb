using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The caller, as taken from the JWT (user name) and the identity store (email).</summary>
public sealed record BillingUser(string UserName, string? Email);

public enum SubscribeStatus
{
    /// <summary>This request created the subscription.</summary>
    Created,
    /// <summary>The user already had this subscription; nothing new was created.</summary>
    Existing,
    /// <summary>The provider's outcome is not yet known; it is settled on a later request.</summary>
    Pending
}

public sealed record SubscribeResult(SubscribeStatus Status, BillingSubscription? Subscription, string? SubscriptionReference, string PlanHandle);

public sealed record MySubscriptionsResult(IReadOnlyList<BillingSubscription> Subscriptions, bool HasBillingAccount, string? PendingSubscriptionReference);

/// <summary>
/// Subscribe flow: plan validated against the catalogue, then for each write: claim (a row in this app's store,
/// refused by primary key on a second caller) → Maxio call → record the result. A refused Maxio call releases the
/// claim; an unknown outcome leaves it Pending, and a later request settles it by re-reading Maxio.
/// </summary>
public class SubscriptionService
{
    private readonly MaxioBillingGateway _gateway;
    private readonly IBillingClaimStore _store;
    private readonly MaxioSettings _settings;
    private readonly MaxioCallPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        MaxioBillingGateway gateway,
        IBillingClaimStore store,
        IOptions<MaxioSettings> settings,
        MaxioCallPolicy policy,
        TimeProvider clock,
        ILogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _store = store;
        _settings = settings.Value;
        _policy = policy;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The Maxio customer reference for an eShop user: stable across restarts (not the Identity row id).</summary>
    public static string CustomerReferenceFor(string userName) => "eshop:" + userName.Trim().ToLowerInvariant();

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken) => _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string? planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            throw new BillingException(401, "The caller could not be identified.");
        }
        var handle = planHandle?.Trim();
        if (string.IsNullOrEmpty(handle))
        {
            throw new BillingException(400, "planHandle is required.");
        }

        using var requestBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestBudget.CancelAfter(_policy.RequestBudget);
        try
        {
            return await SubscribeCoreAsync(user, handle, requestBudget.Token, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request budget expired outside a write (writes return Pending instead). Any claim already taken
            // is either released or left Pending for the next request to settle.
            throw new BillingException(504, "The billing provider did not respond in time; try again shortly.");
        }
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(BillingUser user, string planHandle, CancellationToken token, CancellationToken callerToken)
    {
        // Cross-operation invariant: the plan must be one this application offers (the configured family's catalogue),
        // checked before any claim is taken or any write is made.
        var catalog = await _gateway.ListPlansAsync(token);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.Truncated)
            {
                throw new BillingException(503, "The plan catalogue could not be read completely; try again later.");
            }
            throw new BillingException(400, $"'{planHandle}' is not an available subscription plan.");
        }

        var customerReference = CustomerReferenceFor(user.UserName);
        var customer = await EnsureCustomerAsync(user, customerReference, token, callerToken, allowTakeover: true);
        if (customer is null)
        {
            // Customer creation outcome unknown: its claim stays Pending and the next request settles it.
            return new SubscribeResult(SubscribeStatus.Pending, null, null, plan.Handle);
        }

        return await SubscribeWithClaimAsync(customer.Id, customerReference, plan.Handle, catalog, token, callerToken, allowTakeover: true);
    }

    // ------------------------------------------------------------------ customer

    /// <summary>
    /// Ensures a Maxio customer exists for the user. Returns null when the create's outcome is unknown.
    /// Claim (BillingCustomerLink insert) → ReadCustomerByReference / CreateCustomer → record the id.
    /// </summary>
    private async Task<BillingCustomer?> EnsureCustomerAsync(
        BillingUser user, string customerReference, CancellationToken token, CancellationToken callerToken, bool allowTakeover)
    {
        var claim = new BillingCustomerLink(customerReference, Now);
        if (!await _store.TryClaimCustomerAsync(claim, token))
        {
            return await ResolveExistingCustomerClaimAsync(user, customerReference, token, callerToken, allowTakeover);
        }

        // Claim held. Maxio may already hold this customer (created before a restart wiped the local store).
        BillingCustomer? existing;
        try
        {
            existing = await _gateway.FindCustomerByReferenceAsync(customerReference, token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            await ReleaseQuietlyAsync(claim); // nothing was written to Maxio yet
            throw;
        }

        if (existing is not null)
        {
            await RecordCustomerAsync(claim, existing);
            return existing;
        }

        var email = ResolveEmail(user);
        var (firstName, lastName) = NamesFor(email);

        WriteResult<BillingCustomer> result;
        try
        {
            result = await _gateway.CreateCustomerAsync(customerReference, email, firstName, lastName, token);
        }
        catch (BillingException)
        {
            await ReleaseQuietlyAsync(claim); // Maxio refused: nothing was created
            throw;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            _logger.LogWarning("Customer create for {Reference} hit the request budget; claim left Pending.", customerReference);
            return null;
        }

        if (result.OutcomeUnknown || result.Value is null)
        {
            return null;
        }

        await RecordCustomerAsync(claim, result.Value);
        return result.Value;
    }

    private async Task<BillingCustomer?> ResolveExistingCustomerClaimAsync(
        BillingUser user, string customerReference, CancellationToken token, CancellationToken callerToken, bool allowTakeover)
    {
        var existing = await _store.FindCustomerLinkAsync(customerReference, token);
        if (existing is null)
        {
            // Released between our refused insert and this read: try once more.
            if (allowTakeover)
            {
                return await EnsureCustomerAsync(user, customerReference, token, callerToken, allowTakeover: false);
            }
            throw new DuplicateException("A billing account for this user is being set up; try again shortly.");
        }

        if (existing.Status == BillingClaimStatus.Completed && existing.ProviderCustomerId is int customerId)
        {
            return new BillingCustomer(customerId, existing.CustomerReference);
        }

        if (Now - existing.ClaimedAt < _policy.InFlightWindow)
        {
            throw new DuplicateException("A billing account for this user is being set up; try again shortly.");
        }

        // Stale Pending claim: an earlier create's outcome was never settled. Settle it from Maxio now.
        var reconciliation = await _gateway.ReconcileCustomerAsync(customerReference, token);
        if (reconciliation.Found is not null)
        {
            existing.Complete(reconciliation.Found.Id, Now);
            await _store.UpdateCustomerLinkAsync(existing, CancellationToken.None);
            return reconciliation.Found;
        }

        if (reconciliation.ProvenAbsent && allowTakeover)
        {
            if (!await _store.ReleaseCustomerClaimAsync(existing, token))
            {
                throw new DuplicateException("A billing account for this user is being set up; try again shortly.");
            }
            _logger.LogInformation("Stale customer claim {Reference} proven absent at Maxio; released for a new attempt.", customerReference);
            return await EnsureCustomerAsync(user, customerReference, token, callerToken, allowTakeover: false);
        }

        return null;
    }

    private async Task RecordCustomerAsync(BillingCustomerLink claim, BillingCustomer customer)
    {
        claim.Complete(customer.Id, Now);
        // Recording what Maxio already holds must not be skipped because the request was cancelled.
        if (!await _store.UpdateCustomerLinkAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Customer claim {Reference} changed concurrently while recording Maxio customer {CustomerId}.", claim.CustomerReference, customer.Id);
        }
    }

    // ------------------------------------------------------------------ subscription

    /// <summary>
    /// Claim (SubscriptionClaim insert) → existing-live check → CreateSubscription → record the id.
    /// </summary>
    private async Task<SubscribeResult> SubscribeWithClaimAsync(
        int customerId, string customerReference, string planHandle, PlanCatalog catalog,
        CancellationToken token, CancellationToken callerToken, bool allowTakeover)
    {
        var claim = new SubscriptionClaim(customerReference, NewSubscriptionReference(customerReference), planHandle, Now);
        if (!await _store.TryClaimSubscriptionAsync(claim, token))
        {
            return await ResolveExistingSubscriptionClaimAsync(customerId, customerReference, planHandle, catalog, token, callerToken, allowTakeover);
        }

        // Claim held. A live subscription in this family may already exist (e.g. local store wiped by a restart).
        IReadOnlyList<BillingSubscription> current;
        try
        {
            current = await _gateway.ListCustomerSubscriptionsAsync(customerId, token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            await ReleaseQuietlyAsync(claim); // nothing was written to Maxio yet
            throw;
        }

        var live = current.Where(s => s.IsLive && BelongsToFamily(s, catalog)).ToList();
        var already = live.FirstOrDefault(s => s.PlanHandle == planHandle) ?? live.FirstOrDefault();
        if (already is not null)
        {
            await RecordSubscriptionAsync(claim, already, planHandle);
            if (already.PlanHandle == planHandle)
            {
                return new SubscribeResult(SubscribeStatus.Existing, already, already.Reference, planHandle);
            }
            throw new DuplicateException($"You already have an active subscription to '{already.PlanHandle}'.");
        }

        WriteResult<BillingSubscription> result;
        try
        {
            result = await _gateway.CreateSubscriptionAsync(customerId, planHandle, claim.SubscriptionReference, claim.ClaimedAt, token);
        }
        catch (BillingException)
        {
            await ReleaseQuietlyAsync(claim); // Maxio refused: nothing was created
            throw;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            _logger.LogWarning("Subscription create {Reference} hit the request budget; claim left Pending.", claim.SubscriptionReference);
            return new SubscribeResult(SubscribeStatus.Pending, null, claim.SubscriptionReference, planHandle);
        }

        if (result.OutcomeUnknown || result.Value is null)
        {
            return new SubscribeResult(SubscribeStatus.Pending, null, claim.SubscriptionReference, planHandle);
        }

        await RecordSubscriptionAsync(claim, result.Value, planHandle);
        return new SubscribeResult(SubscribeStatus.Created, result.Value, claim.SubscriptionReference, planHandle);
    }

    private async Task<SubscribeResult> ResolveExistingSubscriptionClaimAsync(
        int customerId, string customerReference, string planHandle, PlanCatalog catalog,
        CancellationToken token, CancellationToken callerToken, bool allowTakeover)
    {
        const string inProgress = "A subscription request for this account is already being processed; try again shortly.";

        var existing = await _store.FindSubscriptionClaimAsync(customerReference, token);
        if (existing is null)
        {
            if (allowTakeover)
            {
                return await SubscribeWithClaimAsync(customerId, customerReference, planHandle, catalog, token, callerToken, allowTakeover: false);
            }
            throw new DuplicateException(inProgress);
        }

        if (existing.Status == BillingClaimStatus.Completed && existing.ProviderSubscriptionId is int subscriptionId)
        {
            var current = (await _gateway.ListCustomerSubscriptionsAsync(customerId, token)).FirstOrDefault(s => s.Id == subscriptionId);
            if (current is { IsLive: true })
            {
                var currentPlan = current.PlanHandle ?? existing.PlanHandle;
                if (currentPlan == planHandle)
                {
                    return new SubscribeResult(SubscribeStatus.Existing, current, current.Reference ?? existing.SubscriptionReference, planHandle);
                }
                throw new DuplicateException($"You already have an active subscription to '{currentPlan}'.");
            }

            // The recorded subscription has ended (or is gone): it no longer blocks a new subscribe.
            if (allowTakeover && await _store.ReleaseSubscriptionClaimAsync(existing, token))
            {
                return await SubscribeWithClaimAsync(customerId, customerReference, planHandle, catalog, token, callerToken, allowTakeover: false);
            }
            throw new DuplicateException(inProgress);
        }

        if (Now - existing.ClaimedAt < _policy.InFlightWindow)
        {
            throw new DuplicateException(inProgress);
        }

        // Stale Pending claim: an earlier create's outcome was never settled. Settle it from Maxio now.
        var reconciliation = await _gateway.ReconcileSubscriptionAsync(customerId, existing.SubscriptionReference, existing.PlanHandle, existing.ClaimedAt, token);
        if (reconciliation.Found is not null)
        {
            var found = reconciliation.Found;
            await RecordSubscriptionAsync(existing, found, existing.PlanHandle);
            if ((found.PlanHandle ?? existing.PlanHandle) == planHandle)
            {
                return new SubscribeResult(SubscribeStatus.Existing, found, existing.SubscriptionReference, planHandle);
            }
            throw new DuplicateException($"You already have an active subscription to '{found.PlanHandle ?? existing.PlanHandle}'.");
        }

        if (reconciliation.ProvenAbsent && allowTakeover)
        {
            if (!await _store.ReleaseSubscriptionClaimAsync(existing, token))
            {
                throw new DuplicateException(inProgress);
            }
            _logger.LogInformation("Stale subscription claim {Reference} proven absent at Maxio; released for a new attempt.", existing.SubscriptionReference);
            return await SubscribeWithClaimAsync(customerId, customerReference, planHandle, catalog, token, callerToken, allowTakeover: false);
        }

        return new SubscribeResult(SubscribeStatus.Pending, null, existing.SubscriptionReference, existing.PlanHandle);
    }

    private async Task RecordSubscriptionAsync(SubscriptionClaim claim, BillingSubscription subscription, string fallbackPlanHandle)
    {
        claim.Complete(subscription.Id, subscription.PlanHandle ?? fallbackPlanHandle, subscription.Reference, Now);
        if (!await _store.UpdateSubscriptionClaimAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Subscription claim {Reference} changed concurrently while recording Maxio subscription {SubscriptionId}.", claim.CustomerReference, subscription.Id);
        }
    }

    // ------------------------------------------------------------------ my subscriptions

    /// <summary>Reads the user's subscriptions in the configured family. Never creates a customer.</summary>
    public async Task<MySubscriptionsResult> GetMySubscriptionsAsync(BillingUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            throw new BillingException(401, "The caller could not be identified.");
        }

        var customerReference = CustomerReferenceFor(user.UserName);
        var claim = await _store.FindSubscriptionClaimAsync(customerReference, cancellationToken);
        var pendingReference = claim?.Status == BillingClaimStatus.Pending ? claim.SubscriptionReference : null;

        var link = await _store.FindCustomerLinkAsync(customerReference, cancellationToken);
        var customerId = link is { Status: BillingClaimStatus.Completed } ? link.ProviderCustomerId : null;
        if (customerId is null)
        {
            var found = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (found is null)
            {
                return new MySubscriptionsResult(Array.Empty<BillingSubscription>(), false, pendingReference);
            }
            customerId = found.Id;
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);

        PlanCatalog? catalog = null;
        if (subscriptions.Any(s => s.ProductFamilyHandle is null))
        {
            catalog = await _gateway.ListPlansAsync(cancellationToken);
        }

        var mine = subscriptions
            .Where(s => BelongsToFamily(s, catalog))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return new MySubscriptionsResult(mine, true, pendingReference);
    }

    // ------------------------------------------------------------------ helpers

    private bool BelongsToFamily(BillingSubscription subscription, PlanCatalog? catalog)
    {
        if (subscription.ProductFamilyHandle is not null)
        {
            return string.Equals(subscription.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.Ordinal);
        }
        return catalog is not null && subscription.PlanHandle is not null
            && catalog.Plans.Any(p => string.Equals(p.Handle, subscription.PlanHandle, StringComparison.Ordinal));
    }

    private static string NewSubscriptionReference(string customerReference) =>
        $"{customerReference}:sub:{Guid.NewGuid():N}";

    private static string ResolveEmail(BillingUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email!.Trim();
        }
        if (user.UserName.Contains('@'))
        {
            return user.UserName.Trim();
        }
        throw new BillingException(400, "The account has no email address, which billing requires.");
    }

    /// <summary>Maxio requires first and last names; eShop accounts only carry an email, so derive them from it.</summary>
    public static (string FirstName, string LastName) NamesFor(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1).Select(Capitalize)) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private async Task ReleaseQuietlyAsync(BillingCustomerLink claim)
    {
        try
        {
            await _store.ReleaseCustomerClaimAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release customer claim {Reference}; it will be settled as stale.", claim.CustomerReference);
        }
    }

    private async Task ReleaseQuietlyAsync(SubscriptionClaim claim)
    {
        try
        {
            await _store.ReleaseSubscriptionClaimAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release subscription claim {Reference}; it will be settled as stale.", claim.CustomerReference);
        }
    }
}
