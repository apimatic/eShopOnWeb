using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscribe flow. Every provider write follows claim → provider call → record: the claim is a row
/// whose primary key refuses a second concurrent caller before it reaches the provider, and its
/// reference lets an ambiguous write be settled later instead of being repeated.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// How long a pending claim is honoured before another request may take it over. Longer than one
    /// request's whole provider budget, so a live holder is never displaced; a crashed one is.
    /// </summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private const string CustomerReferencePrefix = "eshop:";
    private const string SubscriptionReferencePrefix = "eshop-sub-";

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly IClaimStore<BillingCustomerLink> _customerLinks;
    private readonly IClaimStore<SubscriptionClaim> _subscriptionClaims;
    private readonly IBillingSubscriberDirectory _subscribers;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingGateway gateway,
        IClaimStore<BillingCustomerLink> customerLinks,
        IClaimStore<SubscriptionClaim> subscriptionClaims,
        IBillingSubscriberDirectory subscribers,
        TimeProvider clock,
        IAppLogger<SubscriptionService> logger)
    {
        _gateway = gateway;
        _customerLinks = customerLinks;
        _subscriptionClaims = subscriptionClaims;
        _subscribers = subscribers;
        _clock = clock;
        _logger = logger;
    }

    public Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.GetPlanCatalogAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // The plan must be one this store offers, not merely one that exists at the provider.
        var catalog = await _gateway.GetPlanCatalogAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
            ?? throw new UnknownSubscriptionPlanException(planHandle);

        var userKey = NormalizeUserKey(userName);
        var customerId = await EnsureCustomerAsync(userName, userKey, cancellationToken);

        var claim = new SubscriptionClaim(userKey, plan.Handle, NewSubscriptionReference(), Now);
        if (!await _subscriptionClaims.TryAddAsync(claim, cancellationToken))
        {
            var existing = await _subscriptionClaims.FindAsync(new object[] { userKey, plan.Handle }, cancellationToken)
                ?? throw new DuplicateException("A subscription request for this plan is already in progress. Retry shortly.");

            var settled = await ResolveExistingClaimAsync(existing, customerId, cancellationToken);
            if (settled is not null)
            {
                return settled;
            }
            claim = existing;
        }

        return await SubscribeUnderClaimAsync(claim, customerId, cancellationToken);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        var userKey = NormalizeUserKey(userName);

        var link = await _customerLinks.FindAsync(new object[] { userKey }, cancellationToken);
        var customerId = link is { IsLinked: true }
            ? link.BillingCustomerId
            : await _gateway.FindCustomerIdAsync(CustomerReferenceFor(userKey), cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        await SettleUnknownClaimsAsync(userKey, cancellationToken);
        return await _gateway.ListSubscriptionsAsync(customerId.Value, cancellationToken);
    }

    /// <summary>
    /// Handles a subscribe that lost the insert race (or repeats an earlier one).
    /// Returns a result when the request is answered, or null when this request now owns the claim and must subscribe.
    /// </summary>
    private async Task<SubscribeResult?> ResolveExistingClaimAsync(SubscriptionClaim existing, int customerId, CancellationToken cancellationToken)
    {
        var now = Now;
        switch (existing.Status)
        {
            case BillingClaimStatus.Completed:
                {
                    var current = (await _gateway.ListSubscriptionsAsync(customerId, cancellationToken))
                        .FirstOrDefault(s => s.Id == existing.BillingSubscriptionId);
                    if (current is { IsLive: true })
                    {
                        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, current);
                    }

                    // The earlier subscription has ended (or is gone): re-open the claim for a new one.
                    existing.Restart(NewSubscriptionReference(), now);
                    await TakeOverAsync(existing, cancellationToken);
                    return null;
                }

            case BillingClaimStatus.Pending when !existing.IsLeaseExpired(now, ClaimLease):
                throw new DuplicateException("A subscription request for this plan is already in progress. Retry shortly.");

            default:
                {
                    // Unknown, or a pending claim whose holder died: take it over, then settle the
                    // earlier attempt by its reference before deciding to write again.
                    existing.Resume(now);
                    await TakeOverAsync(existing, cancellationToken);

                    BillingSubscription? earlier;
                    try
                    {
                        earlier = await _gateway.FindSubscriptionAsync(existing.Reference, cancellationToken);
                    }
                    catch
                    {
                        await RecordUnknownAsync(existing);
                        throw;
                    }

                    if (earlier is null)
                    {
                        return null;
                    }

                    existing.Complete(earlier.Id, Now);
                    await RecordAsync(existing);
                    _logger.LogInformation("Settled subscription claim {Reference}: subscription {SubscriptionId} exists.", existing.Reference, earlier.Id);
                    return new SubscribeResult(SubscribeOutcome.Created, earlier);
                }
        }
    }

    private async Task<SubscribeResult> SubscribeUnderClaimAsync(SubscriptionClaim claim, int customerId, CancellationToken cancellationToken)
    {
        BillingSubscription? live;
        try
        {
            // Supplementary provider-side check (the claim is the refusal): adopt a live subscription
            // to this plan, e.g. one whose local claim was lost with an in-memory store.
            live = (await _gateway.ListSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s => s.IsLive && string.Equals(s.PlanHandle, claim.PlanHandle, StringComparison.Ordinal));
        }
        catch
        {
            await ReleaseAsync(claim);
            throw;
        }

        if (live is not null)
        {
            claim.Complete(live.Id, Now);
            await RecordAsync(claim);
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, live);
        }

        BillingSubscription created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(customerId, claim.PlanHandle, claim.Reference, cancellationToken);
        }
        catch (BillingOutcomeUnknownException)
        {
            await RecordUnknownAsync(claim);
            _logger.LogWarning("Subscription {Reference} for plan {PlanHandle}: outcome unknown, recorded for settlement.", claim.Reference, claim.PlanHandle);
            return new SubscribeResult(SubscribeOutcome.Pending, null);
        }
        catch (BillingProviderException)
        {
            // A definite refusal: nothing was created, so release the claim for a later attempt.
            await ReleaseAsync(claim);
            throw;
        }
        catch
        {
            // Anything unexpected: keep a record that can be settled rather than forgetting the attempt.
            await RecordUnknownAsync(claim);
            throw;
        }

        claim.Complete(created.Id, Now);
        await RecordAsync(claim);
        _logger.LogInformation("Subscription {SubscriptionId} created for plan {PlanHandle} (reference {Reference}).", created.Id, claim.PlanHandle, claim.Reference);
        return new SubscribeResult(SubscribeOutcome.Created, created);
    }

    private async Task<int> EnsureCustomerAsync(string userName, string userKey, CancellationToken cancellationToken)
    {
        var existing = await _customerLinks.FindAsync(new object[] { userKey }, cancellationToken);
        if (existing is { IsLinked: true })
        {
            return existing.BillingCustomerId!.Value;
        }

        var subscriber = await _subscribers.FindAsync(userName, cancellationToken)
            ?? throw new BillingSubscriberNotFoundException();

        var link = new BillingCustomerLink(userKey, CustomerReferenceFor(userKey), Now);
        if (!await _customerLinks.TryAddAsync(link, cancellationToken))
        {
            var current = await _customerLinks.FindAsync(new object[] { userKey }, cancellationToken);
            if (current is { IsLinked: true })
            {
                return current.BillingCustomerId!.Value;
            }
            if (current is null || !current.IsLeaseExpired(Now, ClaimLease))
            {
                throw new DuplicateException("Your billing account is being set up. Retry shortly.");
            }

            current.Renew(Now);
            if (!await _customerLinks.TryUpdateAsync(current, cancellationToken))
            {
                throw new DuplicateException("Your billing account is being set up. Retry shortly.");
            }
            link = current;
        }

        try
        {
            // The customer reference is unique at the provider, so a customer an earlier (lost)
            // attempt created is found here instead of being created twice.
            var customerId = await _gateway.FindCustomerIdAsync(link.CustomerReference, cancellationToken)
                ?? await _gateway.CreateCustomerAsync(link.CustomerReference, subscriber, cancellationToken);

            link.Link(customerId, Now);
            if (!await _customerLinks.TryUpdateAsync(link, CancellationToken.None))
            {
                _logger.LogWarning("Billing customer link for {CustomerReference} changed concurrently; customer {CustomerId} is still correct.", link.CustomerReference, customerId);
            }
            return customerId;
        }
        catch
        {
            // Safe to release on any failure: a retry looks the customer up by its reference first.
            await _customerLinks.RemoveAsync(link, CancellationToken.None);
            throw;
        }
    }

    private async Task SettleUnknownClaimsAsync(string userKey, CancellationToken cancellationToken)
    {
        var unknown = await _subscriptionClaims.ListAsync(
            new SubscriptionClaimsByStatusSpecification(userKey, BillingClaimStatus.Unknown), cancellationToken);

        foreach (var claim in unknown)
        {
            try
            {
                var found = await _gateway.FindSubscriptionAsync(claim.Reference, cancellationToken);
                if (found is null)
                {
                    continue; // still unknown: the next subscribe for this plan retries under the same reference
                }

                claim.Complete(found.Id, Now);
                await RecordAsync(claim);
                _logger.LogInformation("Settled subscription claim {Reference}: subscription {SubscriptionId} exists.", claim.Reference, found.Id);
            }
            catch (BillingProviderException ex)
            {
                // The listing that follows is authoritative; settlement is retried on the next request.
                _logger.LogWarning("Could not settle subscription claim {Reference}: {Reason}", claim.Reference, ex.Message);
            }
        }
    }

    private async Task TakeOverAsync(SubscriptionClaim claim, CancellationToken cancellationToken)
    {
        if (!await _subscriptionClaims.TryUpdateAsync(claim, cancellationToken))
        {
            throw new DuplicateException("A subscription request for this plan is already in progress. Retry shortly.");
        }
    }

    // Outcomes are recorded with CancellationToken.None: once the provider has acted, a disconnected
    // caller must not stop the record of what happened.
    private async Task RecordAsync(SubscriptionClaim claim)
    {
        if (!await _subscriptionClaims.TryUpdateAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Subscription claim {Reference} changed concurrently while recording its outcome.", claim.Reference);
        }
    }

    private Task RecordUnknownAsync(SubscriptionClaim claim)
    {
        claim.MarkUnknown(Now);
        return RecordAsync(claim);
    }

    private Task ReleaseAsync(SubscriptionClaim claim) => _subscriptionClaims.RemoveAsync(claim, CancellationToken.None);

    private DateTimeOffset Now => _clock.GetUtcNow();

    private static string NormalizeUserKey(string userName) => userName.Trim().ToUpperInvariant();

    private static string CustomerReferenceFor(string userKey) => CustomerReferencePrefix + userKey.ToLowerInvariant();

    private static string NewSubscriptionReference() => SubscriptionReferencePrefix + Guid.NewGuid().ToString("N");
}
