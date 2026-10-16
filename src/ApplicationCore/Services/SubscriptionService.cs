using System;
using System.Collections.Generic;
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
/// Subscribe flow: validate the plan against the catalog on offer, take a durable claim, make sure the buyer has a
/// billing customer, create the subscription, and record the result. Every write follows claim → call → record, so a
/// double-submit is stopped by the claim store before it reaches the billing system.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>How long a claim may stay <see cref="EnrollmentStatus.Pending"/> before it is treated as abandoned.</summary>
    public static readonly TimeSpan PendingLease = TimeSpan.FromMinutes(2);

    /// <summary>How long an unconfirmed enrollment that the billing system cannot find is kept before it is released.</summary>
    public static readonly TimeSpan UnconfirmedReleaseAfter = TimeSpan.FromMinutes(2);

    private const int MaxClaimAttempts = 5;
    private const int SettleBatchSize = 50;

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionClaimStore _store;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _timeProvider;

    public SubscriptionService(ISubscriptionBillingGateway gateway,
        ISubscriptionClaimStore store,
        IAppLogger<SubscriptionService> logger,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _store = store;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    public Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(buyer, nameof(buyer));
        Guard.Against.NullOrWhiteSpace(buyer.BuyerId, nameof(buyer.BuyerId));
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "planHandle is required.");
        }
        planHandle = planHandle.Trim();

        // Only plans of the configured product family may be subscribed to — the catalog listing is the legal set.
        var catalog = await _gateway.ListPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
            ?? throw new BillingException(BillingErrorKind.PlanNotAvailable, $"Plan '{planHandle}' is not available.");

        var family = _gateway.ProductFamilyHandle;
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var generation = await _store.CountEndedEnrollmentsAsync(buyer.BuyerId, family, cancellationToken);
            var claim = new SubscriptionEnrollment(buyer.BuyerId, family, generation, plan.Handle, Now);

            SubscribeResult? result;
            if (await _store.TryClaimSubscriptionAsync(claim, cancellationToken))
            {
                result = await EnrollAsync(claim, buyer, plan, cancellationToken);
            }
            else
            {
                var existing = await _store.GetEnrollmentAsync(claim.ClaimKey, cancellationToken);
                result = existing is null ? null : await ResolveExistingAsync(existing, plan, cancellationToken);
            }

            // null: the generation ended or its claim was released meanwhile — claim again.
            if (result is not null) return result;
        }

        throw new BillingException(BillingErrorKind.InProgress,
            "Another subscription request for this account is being processed. Try again shortly.");
    }

    public async Task<BuyerSubscriptions> GetSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(buyer, nameof(buyer));
        var family = _gateway.ProductFamilyHandle;

        var pending = new List<PendingEnrollment>();
        foreach (var enrollment in await _store.ListEnrollmentsAsync(buyer.BuyerId, family, cancellationToken))
        {
            SubscriptionEnrollment? current = enrollment;
            if (enrollment.NeedsSettlement(Now, PendingLease))
            {
                try
                {
                    current = await SettleAsync(enrollment, cancellationToken);
                }
                catch (BillingException ex)
                {
                    _logger.LogWarning("Could not settle subscription {SubscriptionReference}: {Reason}",
                        enrollment.SubscriptionReference, ex.Message);
                }
            }
            if (current is { Status: EnrollmentStatus.Pending or EnrollmentStatus.Unknown })
            {
                pending.Add(new PendingEnrollment(current.PlanHandle, current.SubscriptionReference, current.ClaimedAt));
            }
        }

        var customerId = await ResolveCustomerIdAsync(buyer, cancellationToken);
        if (customerId is null)
        {
            return new BuyerSubscriptions([], pending);
        }

        var subscriptions = (await _gateway.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken))
            .Where(s => s.ProductFamilyHandle is null || string.Equals(s.ProductFamilyHandle, family, StringComparison.Ordinal))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        pending.RemoveAll(p => subscriptions.Any(s => s.Reference == p.Reference));
        return new BuyerSubscriptions(subscriptions, pending);
    }

    public async Task<int> SettleUnconfirmedAsync(CancellationToken cancellationToken = default)
    {
        var unsettled = await _store.ListUnsettledEnrollmentsAsync(Now - PendingLease, SettleBatchSize, cancellationToken);
        foreach (var enrollment in unsettled)
        {
            if (!enrollment.NeedsSettlement(Now, PendingLease)) continue;
            try
            {
                await SettleAsync(enrollment, cancellationToken);
            }
            catch (BillingException ex)
            {
                _logger.LogWarning("Could not settle subscription {SubscriptionReference}: {Reason}",
                    enrollment.SubscriptionReference, ex.Message);
            }
        }
        return unsettled.Count;
    }

    /// <summary>Runs with the claim held. Returns null when the generation turned out to be ended.</summary>
    private async Task<SubscribeResult?> EnrollAsync(SubscriptionEnrollment claim, BillingBuyer buyer, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        BillingSubscription? existing;
        SubscriptionCreateResult created;
        try
        {
            // Local state can be lost (in-memory store restarts, a lost write); the reference is deterministic,
            // so the billing system may already hold this generation's subscription. Adopt it instead of duplicating.
            existing = await _gateway.FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken);
            if (existing is null)
            {
                var customerId = await EnsureCustomerAsync(buyer, cancellationToken);
                created = await _gateway.CreateSubscriptionAsync(
                    new NewBillingSubscription(claim.SubscriptionReference, plan.Handle, customerId), cancellationToken);
            }
            else
            {
                created = SubscriptionCreateResult.Unknown();
            }
        }
        catch
        {
            // The gateway reports every possibly-landed write as an outcome, not an exception, so nothing exists
            // in the billing system for this claim: release it so the buyer can retry.
            await _store.ReleaseEnrollmentAsync(claim, CancellationToken.None);
            throw;
        }

        if (existing is not null)
        {
            _logger.LogInformation("Adopted existing subscription {SubscriptionId} for reference {SubscriptionReference}",
                existing.Id, claim.SubscriptionReference);
            return await AdoptAsync(claim, existing, plan);
        }

        // The write has been attempted: record its outcome even if the caller has gone away.
        switch (created.Outcome)
        {
            case WriteOutcome.Succeeded:
                var subscription = created.Subscription!;
                claim.MarkActive(subscription.Id, subscription.PlanHandle, Now);
                await _store.UpdateEnrollmentAsync(claim, CancellationToken.None);
                _logger.LogInformation("Created subscription {SubscriptionId} ({PlanHandle}, state {State}) for reference {SubscriptionReference}",
                    subscription.Id, claim.PlanHandle, subscription.State, claim.SubscriptionReference);
                return new SubscribeResult(SubscribeOutcome.Created, claim.PlanHandle, claim.SubscriptionReference, subscription);

            case WriteOutcome.Rejected:
                await _store.ReleaseEnrollmentAsync(claim, CancellationToken.None);
                throw new BillingException(BillingErrorKind.Rejected, "The billing system rejected the subscription.", created.Errors);

            default:
                claim.MarkUnknown(Now);
                await _store.UpdateEnrollmentAsync(claim, CancellationToken.None);
                _logger.LogWarning("Subscription {SubscriptionReference} outcome is unconfirmed; it will be settled by reference",
                    claim.SubscriptionReference);
                return new SubscribeResult(SubscribeOutcome.PendingConfirmation, claim.PlanHandle, claim.SubscriptionReference, null);
        }
    }

    private async Task<SubscribeResult?> AdoptAsync(SubscriptionEnrollment claim, BillingSubscription existing, SubscriptionPlan plan)
    {
        if (existing.IsTerminal)
        {
            claim.MarkEnded(existing.Id, Now);
            await _store.UpdateEnrollmentAsync(claim, CancellationToken.None);
            return null;
        }
        claim.MarkActive(existing.Id, existing.PlanHandle, Now);
        await _store.UpdateEnrollmentAsync(claim, CancellationToken.None);
        return ReplayOrConflict(claim, existing, plan);
    }

    /// <summary>The claim for this generation is held by an earlier request. Returns null to claim again.</summary>
    private async Task<SubscribeResult?> ResolveExistingAsync(SubscriptionEnrollment existing, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        switch (existing.Status)
        {
            case EnrollmentStatus.Ended:
                return null;

            case EnrollmentStatus.Active:
                return await ResolveActiveAsync(existing, plan, cancellationToken);

            case EnrollmentStatus.Pending when !existing.NeedsSettlement(Now, PendingLease):
                throw new BillingException(BillingErrorKind.InProgress,
                    "A subscription request for this account is already being processed. Try again shortly.");

            default:
                var settled = await SettleAsync(existing, cancellationToken);
                if (settled is null) return null;
                if (settled.Status == EnrollmentStatus.Active) return await ResolveActiveAsync(settled, plan, cancellationToken);
                if (settled.Status == EnrollmentStatus.Ended) return null;
                if (!string.Equals(settled.PlanHandle, plan.Handle, StringComparison.Ordinal))
                {
                    throw new BillingException(BillingErrorKind.InProgress,
                        $"A subscription to plan '{settled.PlanHandle}' is awaiting confirmation. Try again shortly.");
                }
                return new SubscribeResult(SubscribeOutcome.PendingConfirmation, settled.PlanHandle, settled.SubscriptionReference, null);
        }
    }

    private async Task<SubscribeResult?> ResolveActiveAsync(SubscriptionEnrollment enrollment, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var live = enrollment.BillingSubscriptionId is int id
            ? await _gateway.ReadSubscriptionAsync(id, cancellationToken)
            : await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, cancellationToken);

        if (live is null || live.IsTerminal)
        {
            enrollment.MarkEnded(live?.Id, Now);
            await _store.UpdateEnrollmentAsync(enrollment, cancellationToken);
            return null;
        }
        return ReplayOrConflict(enrollment, live, plan);
    }

    private static SubscribeResult ReplayOrConflict(SubscriptionEnrollment enrollment, BillingSubscription live, SubscriptionPlan plan)
    {
        if (!string.Equals(enrollment.PlanHandle, plan.Handle, StringComparison.Ordinal))
        {
            throw new BillingException(BillingErrorKind.Conflict,
                $"You already have an active subscription to plan '{enrollment.PlanHandle}'.");
        }
        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, enrollment.PlanHandle, enrollment.SubscriptionReference, live);
    }

    /// <summary>Looks an unconfirmed enrollment up by its reference. Returns null when the claim was released.</summary>
    private async Task<SubscriptionEnrollment?> SettleAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var found = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, cancellationToken);
        if (found is not null)
        {
            if (found.IsTerminal) enrollment.MarkEnded(found.Id, Now);
            else enrollment.MarkActive(found.Id, found.PlanHandle, Now);
            await _store.UpdateEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Settled subscription {SubscriptionReference} as {Status} (subscription {SubscriptionId})",
                enrollment.SubscriptionReference, enrollment.Status, found.Id);
            return enrollment;
        }

        if (Now - enrollment.UpdatedAt >= UnconfirmedReleaseAfter)
        {
            // The billing system never received it: free the claim so the buyer can subscribe again.
            await _store.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogWarning("Released unconfirmed subscription {SubscriptionReference}: not found in the billing system",
                enrollment.SubscriptionReference);
            return null;
        }
        return enrollment;
    }

    private async Task<int> EnsureCustomerAsync(BillingBuyer buyer, CancellationToken cancellationToken)
    {
        var reference = BillingReferences.ForCustomer(buyer.BuyerId);
        var claim = await _store.GetCustomerAsync(buyer.BuyerId, cancellationToken);
        if (claim?.BillingCustomerId is int knownId) return knownId;

        if (claim is null)
        {
            claim = new BillingCustomer(buyer.BuyerId, reference, Now);
            if (!await _store.TryClaimCustomerAsync(claim, cancellationToken))
            {
                var holder = await _store.GetCustomerAsync(buyer.BuyerId, cancellationToken);
                if (holder?.BillingCustomerId is int heldId) return heldId;
                throw new BillingException(BillingErrorKind.InProgress,
                    "Your billing account is being set up by another request. Try again shortly.");
            }
        }
        else if (Now - claim.ClaimedAt < PendingLease)
        {
            throw new BillingException(BillingErrorKind.InProgress,
                "Your billing account is being set up by another request. Try again shortly.");
        }
        else
        {
            // Abandoned claim: take it over. The billing system refuses a second customer with the same reference,
            // so even a racing take-over cannot create a duplicate customer.
            claim.Renew(Now);
            await _store.UpdateCustomerAsync(claim, cancellationToken);
        }

        CustomerCreateResult result;
        try
        {
            var found = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);
            result = found is not null
                ? CustomerCreateResult.Succeeded(found)
                : await _gateway.CreateCustomerAsync(new NewBillingCustomer(reference,
                    FirstNameFor(buyer), LastNameFor(buyer), buyer.Email), cancellationToken);
        }
        catch
        {
            await _store.ReleaseCustomerAsync(claim, CancellationToken.None);
            throw;
        }

        switch (result.Outcome)
        {
            case WriteOutcome.Succeeded:
                claim.LinkBillingCustomer(result.Customer!.Id, Now);
                await _store.UpdateCustomerAsync(claim, CancellationToken.None);
                _logger.LogInformation("Linked billing customer {CustomerId} to reference {CustomerReference}",
                    result.Customer.Id, reference);
                return result.Customer.Id;

            case WriteOutcome.Rejected:
                await _store.ReleaseCustomerAsync(claim, CancellationToken.None);
                throw new BillingException(BillingErrorKind.Rejected, "The billing system rejected the customer details.", result.Errors);

            default:
                // Safe to release: a retry sends the same reference, which the billing system will not duplicate.
                await _store.ReleaseCustomerAsync(claim, CancellationToken.None);
                throw new BillingException(BillingErrorKind.ProviderUnavailable,
                    "The billing system did not confirm your billing account. Try again shortly.");
        }
    }

    private async Task<int?> ResolveCustomerIdAsync(BillingBuyer buyer, CancellationToken cancellationToken)
    {
        var local = await _store.GetCustomerAsync(buyer.BuyerId, cancellationToken);
        if (local?.BillingCustomerId is int id) return id;

        var found = await _gateway.FindCustomerByReferenceAsync(BillingReferences.ForCustomer(buyer.BuyerId), cancellationToken);
        if (found is null) return null;

        if (local is null)
        {
            var link = new BillingCustomer(buyer.BuyerId, BillingReferences.ForCustomer(buyer.BuyerId), Now);
            link.LinkBillingCustomer(found.Id, Now);
            await _store.TryClaimCustomerAsync(link, cancellationToken);
        }
        return found.Id;
    }

    private static string FirstNameFor(BillingBuyer buyer)
    {
        if (!string.IsNullOrWhiteSpace(buyer.FirstName)) return buyer.FirstName.Trim();
        var localPart = buyer.Email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }

    private static string LastNameFor(BillingBuyer buyer) =>
        string.IsNullOrWhiteSpace(buyer.LastName) ? "Customer" : buyer.LastName.Trim();
}
