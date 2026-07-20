using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private static readonly SubscriptionLifecycleState[] ReactivatableStates =
    {
        SubscriptionLifecycleState.Canceled,
        SubscriptionLifecycleState.Unpaid,
        SubscriptionLifecycleState.Expired
    };

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        // Resolve the plan first so a stale/reseeded handle fails fast with a configuration
        // error rather than an opaque provider rejection (UC1 failure scenario).
        await _billingClient.GetPlanByHandleAsync(productHandle, cancellationToken);

        var customer = await _billingClient.EnsureCustomerAsync(userReference, email, firstName, lastName, cancellationToken);

        var existing = await _billingClient.FindLiveSubscriptionAsync(customer.Id, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {0} already has a live subscription {1}; returning it instead of creating a duplicate.", userReference, existing.Id);
            return existing;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);

        await PublishBestEffort(new SubscriptionActivated(userReference, subscription.Id, productHandle), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<UsageResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, string? ownerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (subscription.State != SubscriptionLifecycleState.Active)
        {
            throw new InvalidSubscriptionTransitionException($"Subscription {subscriptionId} is not active (state: {subscription.State}); usage cannot be recorded.");
        }

        var usage = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

        int? balance;
        try
        {
            balance = await _billingClient.GetMeteredUsageBalanceAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Usage recorded for subscription {0} but reading back the period-to-date balance failed: {1}", subscriptionId, ex.Message);
            balance = null;
        }

        return new UsageResult(usage, balance);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, string? ownerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, PlanChangePreview previouslyShownPreview, string? ownerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.Null(previouslyShownPreview, nameof(previouslyShownPreview));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        // Re-preview immediately before commit and require it to match what the customer was
        // shown — never silently apply a different amount than the one previewed (UC3).
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);
        if (freshPreview.TargetPriceInCents != previouslyShownPreview.TargetPriceInCents ||
            freshPreview.ProratedAdjustmentInCents != previouslyShownPreview.ProratedAdjustmentInCents ||
            freshPreview.ChargeInCents != previouslyShownPreview.ChargeInCents)
        {
            throw new PlanChangePreviewStaleException("The previewed amount is no longer valid; request a fresh preview before committing.");
        }

        var oldProductHandle = subscription.ProductHandle ?? string.Empty;

        var updated = applyNow
            ? await _billingClient.CommitPlanChangeNowAsync(subscriptionId, targetProductHandle, cancellationToken)
            : await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetProductHandle, cancellationToken);

        var reference = ownerReference ?? updated.CustomerReference ?? subscription.CustomerReference ?? string.Empty;
        await PublishBestEffort(new SubscriptionPlanChanged(reference, subscriptionId, oldProductHandle, targetProductHandle, applyNow), cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> PauseAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (subscription.State != SubscriptionLifecycleState.Active && subscription.State != SubscriptionLifecycleState.PastDue)
        {
            throw new InvalidSubscriptionTransitionException($"Cannot pause subscription {subscriptionId} from state {subscription.State}.");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeBestEffort(ownerReference, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ResumeAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (subscription.State != SubscriptionLifecycleState.Paused)
        {
            throw new InvalidSubscriptionTransitionException($"Cannot resume subscription {subscriptionId} from state {subscription.State}; it is not paused.");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeBestEffort(ownerReference, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (subscription.State == SubscriptionLifecycleState.Canceled || subscription.State == SubscriptionLifecycleState.Expired)
        {
            throw new InvalidSubscriptionTransitionException($"Subscription {subscriptionId} is already {subscription.State}.");
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChangeBestEffort(ownerReference, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ReactivateAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (!ReactivatableStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionTransitionException($"Cannot reactivate subscription {subscriptionId} from state {subscription.State}.");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeBestEffort(ownerReference, subscription, updated, cancellationToken);
        return updated;
    }

    private static void EnsurePlanChangeIsLegal(BillingSubscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionTransitionException("Target plan is the same as the current plan.");
        }

        if (!subscription.IsLive)
        {
            throw new InvalidSubscriptionTransitionException($"Subscription {subscription.Id} is not active (state: {subscription.State}); it must be reactivated before changing plans.");
        }
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (ownerReference is not null &&
            !string.Equals(subscription.CustomerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionAccessDeniedException(subscriptionId);
        }

        return subscription;
    }

    private Task PublishStateChangeBestEffort(string? ownerReference, BillingSubscription before, BillingSubscription after, CancellationToken cancellationToken)
    {
        var reference = ownerReference ?? after.CustomerReference ?? before.CustomerReference ?? string.Empty;
        return PublishBestEffort(new SubscriptionStateChanged(reference, after.Id, before.State, after.State), cancellationToken);
    }

    private async Task PublishBestEffort(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort, in-process eventing (§2.5): a handler failure never rolls back the
            // subscription action that already succeeded against the provider.
            _logger.LogWarning("In-process notification handler failed for {0}: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
