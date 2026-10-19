using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>The eShopOnWeb user a billing operation is for.</summary>
/// <param name="UserKey">Stable per-user key (normalized user name); primary key of the user's claims.</param>
public record BillingUser(string UserKey, string Email)
{
    /// <summary>The Maxio customer reference. Derived from the user name so it survives identity re-seeding.</summary>
    public string CustomerReference => $"eshop:{UserKey.ToLowerInvariant()}";
}

public record SubscribeResult(SubscriptionInfo Subscription, bool Created);

/// <summary>
/// Orchestrates the Subscribe flow. Every Maxio write follows claim → SDK call → record:
/// the claim row (primary key = user) is saved before the write, so a double-click is refused by the
/// store; a write whose outcome is unknown is settled by re-reading Maxio by the reference it carried.
/// </summary>
public class SubscriptionBillingService
{
    // Total time one API request may spend talking to Maxio.
    internal static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);
    // Independent budget for settling an unknown outcome (runs even if the caller has gone away).
    internal static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    // How long a Pending claim blocks other requests before it may be taken over. Must exceed RequestBudget.
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private readonly MaxioBillingGateway _gateway;
    private readonly IBillingClaimStore _claims;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(MaxioBillingGateway gateway, IBillingClaimStore claims, TimeProvider time, ILogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _claims = claims;
        _time = time;
        _logger = logger;
    }

    public async Task<PlanCatalog> ListPlansAsync(CancellationToken requestAborted)
    {
        using var budget = CreateBudget(requestAborted);
        return await _gateway.ListPlansAsync(budget.Token);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(BillingUser user, CancellationToken requestAborted)
    {
        using var budget = CreateBudget(requestAborted);

        // Maxio is the system of record: if this process has no completed claim (e.g. after a restart),
        // find the customer by its reference instead of reporting "no subscriptions".
        var claim = await _claims.GetAsync<BillingCustomerClaim>(user.UserKey, budget.Token);
        var customerId = claim?.ProviderCustomerId
            ?? await _gateway.FindCustomerIdByReferenceAsync(user.CustomerReference, budget.Token);

        if (customerId is not int id)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        return await _gateway.ListCustomerSubscriptionsAsync(id, budget.Token);
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken requestAborted)
    {
        using var budget = CreateBudget(requestAborted);
        var ct = budget.Token;

        // Invariant: the plan must be one this application offers (a product of the configured family).
        var catalog = await _gateway.ListPlansAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.Truncated)
            {
                throw new BillingException(HttpStatusCode.ServiceUnavailable, "The plan list could not be read completely. Please try again later.");
            }
            throw new BillingException(HttpStatusCode.BadRequest, $"'{planHandle}' is not an available subscription plan.");
        }

        var customerId = await EnsureCustomerAsync(user, ct);
        return await EnsureSubscriptionAsync(user, customerId, plan.Handle, ct);
    }

    internal async Task<int> EnsureCustomerAsync(BillingUser user, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var claim = new BillingCustomerClaim(user.UserKey, user.CustomerReference, now, ClaimLease);

        if (!await _claims.TryInsertAsync(claim, ct))
        {
            // Refused by the primary key: another request already holds (or completed) this user's claim.
            var existing = await _claims.GetAsync<BillingCustomerClaim>(user.UserKey, ct);
            if (existing is { Status: BillingClaimStatus.Completed, ProviderCustomerId: int existingId })
            {
                return existingId;
            }
            if (existing is null || existing.IsLeaseActive(now)
                || !await _claims.TryUpdateAsync(existing, c => c.RenewLease(now, ClaimLease), ct))
            {
                throw new DuplicateException("Billing enrollment for this account is already in progress. Please retry shortly.");
            }
            claim = existing;   // took over an abandoned claim
        }

        // The customer may already exist at Maxio (earlier process, abandoned claim): adopt it by reference.
        var found = await _gateway.FindCustomerIdByReferenceAsync(claim.ProviderReference, ct);
        if (found is int foundId)
        {
            await CompleteAsync(claim, c => c.Complete(foundId, _time.GetUtcNow()));
            return foundId;
        }

        var (firstName, lastName) = NamesFor(user);
        int customerId;
        try
        {
            customerId = await _gateway.CreateCustomerAsync(claim.ProviderReference, firstName, lastName, user.Email, ct);
        }
        catch (MaxioRejectedException rejected) when (rejected.ProviderStatus == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio allows one customer per reference; a 422 may mean a concurrent/earlier create won.
            var winner = await SettleCustomerAsync(claim);
            if (winner is int winnerId)
            {
                return winnerId;
            }
            await _claims.TryReleaseAsync(claim, CancellationToken.None);
            throw;
        }
        catch (MaxioOutcomeUnknownException ex)
        {
            var settled = await SettleCustomerAsync(claim);
            if (settled is int settledId)
            {
                return settledId;
            }
            // Not found at Maxio: the create did not happen. Customer references are unique at Maxio,
            // so releasing the claim and letting the caller retry cannot create a duplicate.
            await _claims.TryReleaseAsync(claim, CancellationToken.None);
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "The billing provider did not confirm the account setup. Please try again.", null, ex);
        }
        catch (BillingException)
        {
            await _claims.TryReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        await CompleteAsync(claim, c => c.Complete(customerId, _time.GetUtcNow()));
        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserKey}.", customerId, user.UserKey);
        return customerId;
    }

    /// <summary>Re-reads Maxio for the claim's customer reference; records and returns the id when found.</summary>
    private async Task<int?> SettleCustomerAsync(BillingCustomerClaim claim)
    {
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        int? found;
        try
        {
            found = await _gateway.FindCustomerIdByReferenceAsync(claim.ProviderReference, reconcile.Token);
        }
        catch (BillingException ex)
        {
            // Still unknown: keep the claim Pending; once its lease expires the next request re-reads by reference.
            _logger.LogError(ex, "Could not settle Maxio customer outcome for reference {Reference}; claim left pending.", claim.ProviderReference);
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Account setup status is unknown. Please retry shortly.", null, ex);
        }

        if (found is int id)
        {
            _logger.LogInformation("Settled Maxio customer for reference {Reference}: {CustomerId}.", claim.ProviderReference, id);
            await CompleteAsync(claim, c => c.Complete(id, _time.GetUtcNow()));
        }
        return found;
    }

    internal async Task<SubscribeResult> EnsureSubscriptionAsync(BillingUser user, int customerId, string planHandle, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var claim = new BillingSubscriptionClaim(user.UserKey, planHandle, $"eshop-sub:{Guid.NewGuid():N}", now, ClaimLease);

        if (!await _claims.TryInsertAsync(claim, ct))
        {
            // Refused by the primary key: this user already has (or is creating) a subscription.
            var existing = await _claims.GetAsync<BillingSubscriptionClaim>(user.UserKey, ct);
            if (existing is { Status: BillingClaimStatus.Completed, ProviderSubscriptionId: int subscriptionId })
            {
                return await ExistingSubscriptionAsync(subscriptionId, customerId, planHandle, ct);
            }
            if (existing is null || existing.IsLeaseActive(now)
                || !await _claims.TryUpdateAsync(existing, c => { c.RenewLease(now, ClaimLease); c.ChangePlan(planHandle); }, ct))
            {
                throw new DuplicateException("A subscription for this account is already being created. Please retry shortly.");
            }
            claim = existing;   // took over an abandoned claim

            // An earlier attempt may have created it at Maxio before its outcome was lost.
            var earlier = await _gateway.FindSubscriptionByReferenceAsync(claim.ProviderReference, ct);
            if (earlier is not null)
            {
                await CompleteAsync(claim, c => c.Complete(earlier.Id, earlier.PlanHandle ?? planHandle, _time.GetUtcNow()));
                return new SubscribeResult(earlier, Created: true);
            }
        }

        // The claim store does not survive a restart under the in-memory database: before creating, adopt a
        // live subscription Maxio already holds for this customer rather than creating a second one.
        var live = (await _gateway.ListCustomerSubscriptionsAsync(customerId, ct)).FirstOrDefault(s => s.IsLive);
        if (live is not null)
        {
            await CompleteAsync(claim, c => c.Complete(live.Id, live.PlanHandle ?? planHandle, _time.GetUtcNow()));
            _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for user {UserKey}.", live.Id, user.UserKey);
            return EnsureSamePlan(live, planHandle);
        }

        SubscriptionInfo created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(customerId, planHandle, claim.ProviderReference, ct);
        }
        catch (MaxioOutcomeUnknownException ex)
        {
            created = await SettleSubscriptionAsync(claim, planHandle, ex);
        }
        catch (BillingException)
        {
            // Maxio refused the subscription: release the claim so the user can try again.
            await _claims.TryReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        await CompleteAsync(claim, c => c.Complete(created.Id, created.PlanHandle ?? planHandle, _time.GetUtcNow()));
        _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Plan}, {State}) for user {UserKey}.", created.Id, created.PlanHandle, created.State, user.UserKey);
        return new SubscribeResult(created, Created: true);
    }

    /// <summary>
    /// Settles a CreateSubscription whose outcome is unknown by finding it by the reference it carried.
    /// Found → it was created. Not found → it was not; release the claim. Re-read fails → leave Pending.
    /// </summary>
    private async Task<SubscriptionInfo> SettleSubscriptionAsync(BillingSubscriptionClaim claim, string planHandle, MaxioOutcomeUnknownException cause)
    {
        using var reconcile = new CancellationTokenSource(ReconcileBudget);
        SubscriptionInfo? found;
        try
        {
            found = await _gateway.FindSubscriptionByReferenceAsync(claim.ProviderReference, reconcile.Token);
        }
        catch (BillingException ex)
        {
            _logger.LogError(ex, "Could not settle Maxio subscription outcome for reference {Reference}; claim left pending.", claim.ProviderReference);
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Subscription status is unknown. Please retry shortly; you will not be charged twice.", null, cause);
        }

        if (found is null)
        {
            _logger.LogWarning("Maxio subscription with reference {Reference} was not created; releasing claim.", claim.ProviderReference);
            await _claims.TryReleaseAsync(claim, CancellationToken.None);
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "The subscription was not created. Please try again.", null, cause);
        }

        _logger.LogInformation("Settled Maxio subscription for reference {Reference}: {SubscriptionId}.", claim.ProviderReference, found.Id);
        return found;
    }

    private async Task<SubscribeResult> ExistingSubscriptionAsync(int subscriptionId, int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId, ct);
        var current = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
        if (current is null)
        {
            throw new BillingException(HttpStatusCode.Conflict, "This account's subscription could not be found at the billing provider.");
        }
        return EnsureSamePlan(current, planHandle);
    }

    private static SubscribeResult EnsureSamePlan(SubscriptionInfo existing, string planHandle)
    {
        if (!string.Equals(existing.PlanHandle, planHandle, StringComparison.Ordinal))
        {
            throw new DuplicateException($"This account is already subscribed to plan '{existing.PlanHandle}'. Plan changes are not supported.");
        }
        // Idempotent repeat of the same request: return the existing subscription.
        return new SubscribeResult(existing, Created: false);
    }

    private async Task CompleteAsync<T>(T claim, Action<T> complete) where T : BillingClaim
    {
        // Recording must not be skipped because the caller went away: the provider write already happened.
        if (!await _claims.TryUpdateAsync(claim, complete, CancellationToken.None))
        {
            _logger.LogWarning("Billing claim for {UserKey} changed concurrently while recording the result.", claim.UserKey);
        }
    }

    private CancellationTokenSource CreateBudget(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(RequestBudget);
        return cts;
    }

    private static (string FirstName, string LastName) NamesFor(BillingUser user)
    {
        // ApplicationUser has no name fields; Maxio requires both. Use the e-mail local part.
        var at = user.Email.IndexOf('@');
        var local = at > 0 ? user.Email[..at] : user.Email;
        return (local, "eShopOnWeb");
    }
}
