using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates recurring-subscription enrollment. The billing provider is the system of record;
/// the local <see cref="SubscriptionEnrollment"/> claim keeps concurrent requests for one buyer from
/// creating two customers or two subscriptions.
/// Order for every write: claim → provider call → record the result.
/// </summary>
public class SubscriptionService
{
    /// <summary>Longer than the worst-case duration of one subscribe request, so a live request is never taken over.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingGateway gateway, ISubscriptionEnrollmentStore store,
        TimeProvider clock, IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The provider customer reference for an eShop buyer — deterministic, so it survives restarts.</summary>
    public static string CustomerReferenceFor(string buyerId) => $"eshop-{buyerId}";

    public Task<BillingPlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscribeOutcome> SubscribeAsync(BuyerProfile buyer, string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.Null(buyer, nameof(buyer));
        Guard.Against.NullOrWhiteSpace(buyer.BuyerId, nameof(buyer.BuyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Invariant: the plan must be one the configured product family offers.
        var catalog = await _gateway.GetPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.Ordinal));
        if (plan is null)
        {
            if (catalog.IsTruncated)
            {
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The plan catalog is too large to validate the requested plan.");
            }
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        // Claim first: the store refuses a second claim for the same buyer.
        var enrollment = SubscriptionEnrollment.Claim(buyer.BuyerId, plan.Handle, _clock.GetUtcNow());
        var existing = await _store.TryClaimAsync(enrollment, cancellationToken);
        if (existing is not null)
        {
            var settled = await ResolveExistingClaimAsync(existing, plan, catalog, cancellationToken);
            if (settled is not null)
            {
                return settled;
            }
            enrollment = existing; // taken over; continue the enrollment under the same reference
        }

        try
        {
            return SubscribeOutcome.Confirmed(await EnrollAsync(enrollment, buyer, plan, catalog, cancellationToken));
        }
        catch (BillingOutcomeUnknownException ex)
        {
            // The provider may have acted. Keep the claim, marked unknown; the next request for this
            // buyer settles it by looking the subscription up by its reference (ReconcileAsync).
            _logger.LogWarning("Subscription outcome unknown for reference {0}: {1}", enrollment.SubscriptionReference, ex.Message);
            enrollment.MarkOutcomeUnknown(_clock.GetUtcNow());
            await _store.SaveAsync(enrollment, CancellationToken.None);
            return SubscribeOutcome.Unknown();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && enrollment.Status != EnrollmentStatus.Active)
        {
            // Definite failure (provider refusal, misconfiguration): release so the buyer can retry.
            // An Active claim (e.g. an adopted subscription to another plan) is kept.
            await _store.ReleaseAsync(enrollment, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Caller went away mid-flight: the provider may have acted, so do not release.
            enrollment.MarkOutcomeUnknown(_clock.GetUtcNow());
            await _store.SaveAsync(enrollment, CancellationToken.None);
            throw;
        }
    }

    public async Task<MySubscriptions> GetMySubscriptionsAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var enrollment = await _store.GetAsync(buyerId, cancellationToken);
        var customer = await _gateway.FindCustomerByReferenceAsync(CustomerReferenceFor(buyerId), cancellationToken);
        if (customer is null)
        {
            return new MySubscriptions(Array.Empty<BillingSubscription>(), enrollment is { Status: not EnrollmentStatus.Active });
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        // Settle an unknown enrollment when the provider list shows the write did land.
        if (enrollment is { Status: not EnrollmentStatus.Active })
        {
            var landed = subscriptions.FirstOrDefault(s => s.Reference == enrollment.SubscriptionReference);
            if (landed is not null && enrollment.Status == EnrollmentStatus.OutcomeUnknown)
            {
                enrollment.AttachCustomer(customer.Id, _clock.GetUtcNow());
                enrollment.Complete(landed.Id, landed.PlanHandle ?? enrollment.PlanHandle, _clock.GetUtcNow());
                await _store.SaveAsync(enrollment, cancellationToken);
            }
        }

        var ordered = subscriptions
            .OrderByDescending(s => s.IsLive)
            .ThenByDescending(s => s.CreatedAt)
            .ToList();
        return new MySubscriptions(ordered, enrollment is { Status: not EnrollmentStatus.Active });
    }

    /// <returns>A final outcome, or null when the claim was taken over and enrollment should continue.</returns>
    private async Task<SubscribeOutcome?> ResolveExistingClaimAsync(SubscriptionEnrollment existing, BillingPlan plan,
        BillingPlanCatalog catalog, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        if (existing.Status == EnrollmentStatus.Active)
        {
            if (!string.Equals(existing.PlanHandle, plan.Handle, StringComparison.Ordinal))
            {
                throw new SubscriptionConflictException($"You are already subscribed to plan '{existing.PlanHandle}'.");
            }
            var current = await _gateway.FindSubscriptionByReferenceAsync(existing.SubscriptionReference, cancellationToken);
            if (current is not null)
            {
                return SubscribeOutcome.Confirmed(new SubscribeResult(current, Created: false));
            }
            // Recorded locally but not under our reference (adopted subscription): fall back to the customer's list.
            var adopted = await FindAdoptedAsync(existing, cancellationToken);
            if (adopted is not null)
            {
                return SubscribeOutcome.Confirmed(new SubscribeResult(adopted, Created: false));
            }
            throw new BillingProviderException(BillingFailureKind.Unavailable,
                "Your subscription is recorded but could not be read from the billing provider.");
        }

        if (existing.Status == EnrollmentStatus.Pending && !existing.IsLeaseExpired(now, LeaseDuration))
        {
            throw new SubscriptionConflictException("A subscription request for your account is already in progress.");
        }

        // Stale pending claim or unknown outcome: take it over atomically, then settle what the earlier attempt did.
        if (!await _store.TryTakeOverAsync(existing, plan.Handle, cancellationToken))
        {
            throw new SubscriptionConflictException("A subscription request for your account is already in progress.");
        }

        var reconciled = await ReconcileAsync(existing, cancellationToken);
        if (reconciled is null)
        {
            return null;
        }

        existing.Complete(reconciled.Id, reconciled.PlanHandle ?? existing.PlanHandle, _clock.GetUtcNow());
        await _store.SaveAsync(existing, cancellationToken);
        if (reconciled.PlanHandle is not null && !string.Equals(reconciled.PlanHandle, plan.Handle, StringComparison.Ordinal)
            && catalog.Plans.Any(p => p.Handle == reconciled.PlanHandle))
        {
            throw new SubscriptionConflictException($"You are already subscribed to plan '{reconciled.PlanHandle}'.");
        }
        return SubscribeOutcome.Confirmed(new SubscribeResult(reconciled, Created: false));
    }

    /// <summary>Looks up the subscription an earlier attempt may have created, by the reference it sent.</summary>
    private async Task<BillingSubscription?> ReconcileAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        BillingSubscription? found;
        try
        {
            found = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            throw new BillingOutcomeUnknownException("The earlier subscription attempt could not be confirmed yet.", ex);
        }

        if (found is not null && enrollment.BillingCustomerId is int customerId && found.CustomerId is int foundCustomer
            && foundCustomer != customerId)
        {
            throw new BillingProviderException(BillingFailureKind.Misconfigured,
                "A subscription with this enrollment's reference belongs to a different customer.");
        }
        return found;
    }

    private async Task<BillingSubscription?> FindAdoptedAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        if (enrollment.BillingCustomerId is not int customerId || enrollment.BillingSubscriptionId is not int subscriptionId)
        {
            return null;
        }
        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
    }

    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment enrollment, BuyerProfile buyer, BillingPlan plan,
        BillingPlanCatalog catalog, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(buyer, cancellationToken);
        enrollment.AttachCustomer(customer.Id, _clock.GetUtcNow());
        await _store.SaveAsync(enrollment, cancellationToken);

        // The provider is the system of record: a live subscription to one of our plans (e.g. created
        // before this process restarted and lost its local claims) is adopted, never duplicated.
        var existing = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsLive && s.PlanHandle is not null && catalog.Plans.Any(p => p.Handle == s.PlanHandle));
        if (live is not null)
        {
            enrollment.Complete(live.Id, live.PlanHandle!, _clock.GetUtcNow());
            await _store.SaveAsync(enrollment, cancellationToken);
            if (!string.Equals(live.PlanHandle, plan.Handle, StringComparison.Ordinal))
            {
                throw new SubscriptionConflictException($"You are already subscribed to plan '{live.PlanHandle}'.");
            }
            return new SubscribeResult(live, Created: false);
        }

        var created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle, enrollment.SubscriptionReference, cancellationToken);
        enrollment.Complete(created.Id, plan.Handle, _clock.GetUtcNow());
        await _store.SaveAsync(enrollment, CancellationToken.None);
        _logger.LogInformation("Buyer subscribed to {0} (subscription {1}, reference {2}).", plan.Handle, created.Id, enrollment.SubscriptionReference);
        return new SubscribeResult(created, Created: true);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(BuyerProfile buyer, CancellationToken cancellationToken)
    {
        var reference = CustomerReferenceFor(buyer.BuyerId);
        var customer = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = NamesFor(buyer);
        try
        {
            return await _gateway.CreateCustomerAsync(new NewBillingCustomer(reference, firstName, lastName, buyer.Email), cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.Rejected)
        {
            // The provider allows one customer per reference: a concurrent creator (another host) may have won.
            var winner = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }
            throw;
        }
    }

    private static (string FirstName, string LastName) NamesFor(BuyerProfile buyer)
    {
        var first = string.IsNullOrWhiteSpace(buyer.FirstName) ? null : buyer.FirstName.Trim();
        var last = string.IsNullOrWhiteSpace(buyer.LastName) ? null : buyer.LastName.Trim();
        if (first is null)
        {
            var localPart = buyer.Email.Split('@')[0];
            first = string.IsNullOrWhiteSpace(localPart) ? buyer.BuyerId : localPart;
        }
        return (first, last ?? "eShop Customer");
    }
}
