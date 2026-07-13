using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    // Provider-side states in which a customer is already effectively enrolled — used to make
    // SubscribeAsync idempotent (UC1 failure scenarios: never create a second enrollment).
    private static readonly SubscriptionLifecycleState[] AlreadyEnrolledStates =
    {
        SubscriptionLifecycleState.Active,
        SubscriptionLifecycleState.Trialing,
        SubscriptionLifecycleState.Assessing,
        SubscriptionLifecycleState.PastDue,
        SubscriptionLifecycleState.SoftFailure,
        SubscriptionLifecycleState.Unpaid,
        SubscriptionLifecycleState.OnHold
    };

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher)
    {
        _billingClient = billingClient;
        _publisher = publisher;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var customerId = await _billingClient.FindOrCreateCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var alreadyEnrolled = existingSubscriptions.FirstOrDefault(s => AlreadyEnrolledStates.Contains(s.State));
        if (alreadyEnrolled is not null)
        {
            return alreadyEnrolled;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customerId, productHandle, cancellationToken);

        await _publisher.Publish(
            new SubscriptionActivated(customerReference, subscription.Id, subscription.ProductHandle, subscription.ProductName, subscription.ProductPriceInCents),
            cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var customerId = await _billingClient.TryFindCustomerIdAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
    }

    public async Task<UsageRecordResult> RecordUsageAsync(string customerReference, bool actingAsAdmin, long subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidUsageQuantityException(quantity);
        }

        // UC2 precondition: re-verified before every first usage call, not just at startup, so a
        // sandbox reseed that changes the component's kind is caught here rather than surfacing as
        // a confusing provider error.
        await _billingClient.ValidateMeteredComponentAsync(cancellationToken);

        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        if (subscription.State != SubscriptionLifecycleState.Active)
        {
            throw new SubscriptionNotActiveException(subscriptionId, subscription.State);
        }

        var usageId = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

        int? periodToDateTotal;
        try
        {
            periodToDateTotal = await _billingClient.TryGetPeriodToDateUsageAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // Usage was recorded successfully; only the read-back failed (UC2 failure scenarios).
            periodToDateTotal = null;
        }

        return new UsageRecordResult(usageId, quantity, periodToDateTotal);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string customerReference, bool actingAsAdmin, long subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        ValidatePlanChangeRequest(subscription, targetProductHandle, timing);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(string customerReference, bool actingAsAdmin, long subscriptionId, string targetProductHandle, PlanChangeTiming timing, long expectedProratedAdjustmentInCents, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        ValidatePlanChangeRequest(subscription, targetProductHandle, timing);

        // Re-preview immediately before committing so a price/proration-basis change between the
        // customer's preview and this confirm is caught rather than silently applied (UC3 failure scenarios).
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);
        if (freshPreview.ProratedAdjustmentInCents != expectedProratedAdjustmentInCents)
        {
            throw new StalePlanChangePreviewException(expectedProratedAdjustmentInCents, freshPreview.ProratedAdjustmentInCents);
        }

        var fromProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);

        await _publisher.Publish(
            new SubscriptionPlanChanged(subscription.CustomerReference, subscriptionId, fromProductHandle, targetProductHandle, freshPreview.ProratedAdjustmentInCents),
            cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        if (subscription.State != SubscriptionLifecycleState.Active)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "pause");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        // Maxio's PauseSubscription transitions a subscription to OnHold, not the separate Paused
        // wire value (confirmed against the live sandbox and the SDK's own "resumes a paused
        // (on-hold) subscription" doc-comment) — OnHold is therefore the legal precondition here.
        if (subscription.State != SubscriptionLifecycleState.OnHold)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "resume");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        if (subscription.State is SubscriptionLifecycleState.Canceled or SubscriptionLifecycleState.Expired)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "cancel");
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChangeAsync(subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, actingAsAdmin, subscriptionId, cancellationToken);
        if (subscription.State is not (SubscriptionLifecycleState.Canceled or SubscriptionLifecycleState.Unpaid or SubscriptionLifecycleState.TrialEnded))
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "reactivate");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(subscription, updated, cancellationToken);
        return updated;
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!actingAsAdmin && !string.Equals(subscription.CustomerReference, customerReference, StringComparison.OrdinalIgnoreCase))
        {
            // Don't reveal that a subscription id exists for a different customer.
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private static void ValidatePlanChangeRequest(BillingSubscription subscription, string targetProductHandle, PlanChangeTiming timing)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            throw new PlanChangeNotSupportedException(
                "Committing a plan change at the next renewal without proration is not supported: the Maxio Advanced Billing .NET SDK's subscription-migration operation has no field that defers a commit — every combination of its proration flags bills immediately. Use PlanChangeTiming.Immediate.");
        }

        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new PlanChangeNotSupportedException($"Subscription {subscription.Id} is already on plan '{targetProductHandle}'.");
        }

        if (subscription.State is SubscriptionLifecycleState.Canceled or SubscriptionLifecycleState.Expired or SubscriptionLifecycleState.Suspended)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.State, "change plan on");
        }
    }

    private Task PublishStateChangeAsync(BillingSubscription previous, BillingSubscription updated, CancellationToken cancellationToken)
        => _publisher.Publish(new SubscriptionStateChanged(previous.CustomerReference, updated.Id, previous.State, updated.State), cancellationToken);
}
