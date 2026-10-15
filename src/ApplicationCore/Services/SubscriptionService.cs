using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Recurring-subscription enrollment. The billing system is the system of record; the local
/// <see cref="SubscriptionEnrollment"/> row is only a claim that keeps a double-submit from
/// reaching the provider twice, and a marker for outcomes that still need settling.
/// One subscription per shopper per plan catalog.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    public static readonly TimeSpan DefaultClaimLease = TimeSpan.FromMinutes(2);

    private const string CustomerLastName = "eShopOnWeb";

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly ISubscriberDirectory _subscribers;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeSpan _claimLease;

    public SubscriptionService(ISubscriptionBillingGateway gateway,
        ISubscriptionEnrollmentStore store,
        ISubscriberDirectory subscribers,
        TimeProvider clock,
        IAppLogger<SubscriptionService> logger,
        TimeSpan? claimLease = null)
    {
        _gateway = gateway;
        _store = store;
        _subscribers = subscribers;
        _clock = clock;
        _logger = logger;
        _claimLease = claimLease ?? DefaultClaimLease;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var subscriber = await _subscribers.FindByUserNameAsync(userName) ?? throw new SubscriberNotFoundException();

        // The plan must be one this application offers, not merely one the provider knows.
        var catalog = await _gateway.ListPlansAsync(cancellationToken);
        var handle = string.IsNullOrWhiteSpace(planHandle) ? catalog.DefaultPlanHandle : planHandle.Trim();
        var plan = handle is null ? null : catalog.Find(handle);
        if (plan is null)
        {
            if (handle is not null && !catalog.IsComplete)
            {
                throw new BillingUnavailableException("The plan catalog could not be read completely. Please try again later.");
            }
            throw new SubscriptionPlanNotOfferedException(handle);
        }

        var references = BillingReferences.For(subscriber.UserName, _gateway.CatalogKey);
        var claim = new SubscriptionEnrollment(references.EnrollmentKey, references.CustomerReference,
            references.SubscriptionReference, plan.Handle, Now);

        if (await _store.TryClaimAsync(claim, cancellationToken))
        {
            return await EnrollAsync(claim, subscriber, plan, cancellationToken);
        }

        var existing = await _store.GetAsync(references.EnrollmentKey, cancellationToken)
            ?? throw new DuplicateException("Another subscription request for this account just finished. Please retry.");
        return await ResolveExistingClaimAsync(existing, subscriber, plan, cancellationToken);
    }

    public async Task<CustomerSubscriptions> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var references = BillingReferences.For(userName, _gateway.CatalogKey);
        var claim = await _store.GetAsync(references.EnrollmentKey, cancellationToken);

        string? pendingPlanHandle = null;
        if (claim is not null && !claim.IsCompleted)
        {
            var stale = claim.IsStale(Now, _claimLease);
            if (claim.Status == SubscriptionEnrollmentStatus.OutcomeUnknown || stale)
            {
                var settled = await SettleAsync(claim, cancellationToken);
                // A stale claim the provider has no subscription for never completed: nothing pending.
                if (settled is null && !stale)
                {
                    pendingPlanHandle = claim.PlanHandle;
                }
            }
            else
            {
                pendingPlanHandle = claim.PlanHandle;
            }
        }

        var customerId = claim?.BillingCustomerId
            ?? await _gateway.FindCustomerIdAsync(references.CustomerReference, cancellationToken);
        if (customerId is null)
        {
            return new CustomerSubscriptions(Array.Empty<BillingSubscription>(), pendingPlanHandle);
        }

        var subscriptions = await _gateway.ListSubscriptionsForCustomerAsync(customerId.Value, cancellationToken);
        return new CustomerSubscriptions(subscriptions, pendingPlanHandle);
    }

    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment claim, Subscriber subscriber,
        SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        int? customerId = claim.BillingCustomerId;
        try
        {
            customerId = await _gateway.EnsureCustomerAsync(new BillingCustomer(claim.CustomerReference,
                subscriber.Email, FirstNameFor(subscriber), CustomerLastName), cancellationToken);

            // The provider may already hold this subscription (e.g. a claim lost with an in-memory store).
            var existing = await _gateway.FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await CompleteAsync(claim, customerId, existing, plan.Handle);
                return AlreadySubscribed(claim, existing, plan);
            }

            var created = await _gateway.CreateSubscriptionAsync(customerId.Value, plan.Handle,
                claim.SubscriptionReference, cancellationToken);
            await CompleteAsync(claim, customerId, created, plan.Handle);
            _logger.LogInformation("Subscription {SubscriptionId} created on plan {PlanHandle} with state {State}.",
                created.Id, plan.Handle, created.State);
            return new SubscribeResult(SubscribeOutcome.Created, created, plan.Handle);
        }
        catch (BillingOutcomeUnknownException ex)
        {
            _logger.LogWarning("Subscription {SubscriptionReference} outcome unknown; it will be settled on the next request. {Reason}",
                claim.SubscriptionReference, ex.Message);
            claim.MarkOutcomeUnknown(customerId, Now);
            await _store.TryUpdateAsync(claim, CancellationToken.None);
            return new SubscribeResult(SubscribeOutcome.Pending, null, plan.Handle);
        }
        catch (OperationCanceledException)
        {
            // The request was abandoned mid-flight and a create may already have been sent.
            claim.MarkOutcomeUnknown(customerId, Now);
            await _store.TryUpdateAsync(claim, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is BillingRejectedException or BillingUnavailableException)
        {
            // The provider refused (or was never reached): no subscription exists, so free the claim.
            await _store.ReleaseAsync(claim, CancellationToken.None);
            throw;
        }
    }

    private async Task<SubscribeResult> ResolveExistingClaimAsync(SubscriptionEnrollment existing, Subscriber subscriber,
        SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        if (existing.IsCompleted)
        {
            var subscription = await _gateway.FindSubscriptionByReferenceAsync(existing.SubscriptionReference, cancellationToken);
            return AlreadySubscribed(existing, subscription, plan);
        }

        var stale = existing.IsStale(Now, _claimLease);
        if (existing.Status == SubscriptionEnrollmentStatus.Pending && !stale)
        {
            throw new DuplicateException("A subscription request for this account is already in progress.");
        }

        var settled = await SettleAsync(existing, cancellationToken);
        if (settled is not null)
        {
            return AlreadySubscribed(existing, settled, plan);
        }

        if (!stale)
        {
            // Outcome still unknown and too recent to assume the create never landed.
            return new SubscribeResult(SubscribeOutcome.Pending, null, existing.PlanHandle);
        }

        // Stale and confirmed absent at the provider: take the claim over. The version check makes
        // the take-over itself a claim, so two callers cannot both restart it.
        existing.Restart(plan.Handle, Now);
        if (!await _store.TryUpdateAsync(existing, cancellationToken))
        {
            throw new DuplicateException("A subscription request for this account is already in progress.");
        }
        return await EnrollAsync(existing, subscriber, plan, cancellationToken);
    }

    /// <summary>Re-reads the provider for the claim's subscription and records it when found.</summary>
    private async Task<BillingSubscription?> SettleAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken)
    {
        var subscription = await _gateway.FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var customerId = claim.BillingCustomerId
            ?? await _gateway.FindCustomerIdAsync(claim.CustomerReference, cancellationToken);
        await CompleteAsync(claim, customerId, subscription, claim.PlanHandle);
        _logger.LogInformation("Settled subscription {SubscriptionReference}: provider holds subscription {SubscriptionId}.",
            claim.SubscriptionReference, subscription.Id);
        return subscription;
    }

    private async Task CompleteAsync(SubscriptionEnrollment claim, int? customerId, BillingSubscription subscription, string fallbackPlanHandle)
    {
        claim.MarkCompleted(customerId, subscription.Id, subscription.PlanHandle ?? fallbackPlanHandle, Now);
        if (!await _store.TryUpdateAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Enrollment {EnrollmentKey} was changed concurrently while recording subscription {SubscriptionId}.",
                claim.EnrollmentKey, subscription.Id);
        }
    }

    private static SubscribeResult AlreadySubscribed(SubscriptionEnrollment claim, BillingSubscription? subscription, SubscriptionPlan requested)
    {
        var heldPlan = subscription?.PlanHandle ?? claim.PlanHandle;
        if (!string.Equals(heldPlan, requested.Handle, StringComparison.Ordinal))
        {
            throw new DuplicateException($"You are already subscribed to plan '{heldPlan}'. Changing plans is not supported.");
        }
        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, subscription, heldPlan);
    }

    private static string FirstNameFor(Subscriber subscriber)
    {
        var at = subscriber.Email.IndexOf('@');
        var localPart = at > 0 ? subscriber.Email[..at] : subscriber.UserName;
        return string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart;
    }
}

/// <summary>
/// Deterministic references shared with the billing provider. The customer reference is derived
/// from the user name (unique in Identity) without exposing it: no e-mail in provider URLs or logs.
/// </summary>
public record BillingReferences(string CustomerReference, string SubscriptionReference, string EnrollmentKey)
{
    public static BillingReferences For(string userName, string catalogKey)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(catalogKey, nameof(catalogKey));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant()));
        var customerReference = "eshop-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        return new BillingReferences(
            customerReference,
            $"{customerReference}-{catalogKey}",
            $"{catalogKey}:{customerReference}");
    }
}
