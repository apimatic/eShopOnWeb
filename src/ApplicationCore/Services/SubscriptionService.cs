using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the eShopOnWeb Subscribe use cases (mirrors <see cref="OrderService"/>): validates,
/// calls the single <see cref="IBillingClient"/> seam, and publishes the corresponding MediatR
/// notification best-effort (plan.md §2.5) after each successful provider call.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userId, string userEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(userEmail, nameof(userEmail));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var existingSubscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(userId, cancellationToken);
        var alreadyActive = existingSubscriptions.FirstOrDefault(s => s.IsActiveOrTrialing);
        if (alreadyActive is not null)
        {
            // UC1: duplicate subscribe (double-click, repeated call) — never create a second enrollment.
            return alreadyActive;
        }

        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => p.Handle == productHandle))
        {
            throw new BillingConfigurationException(
                $"Configured product handle '{productHandle}' does not resolve to a plan in the billing provider; check the seed (UC0).");
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(userId, userEmail, productHandle, cancellationToken);

        await PublishBestEffort(new SubscriptionActivated(userId, subscription.Id, subscription.ProductHandle), cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        return _billingClient.ListSubscriptionsForCustomerAsync(userId, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerUserId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!subscription.IsActiveOrTrialing)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "record usage against");
        }

        // First-call validation (UC2 precondition): the configured component handle must still
        // resolve to a metered-kind component before any usage is sent to the provider.
        await _billingClient.EnsureMeteredComponentAsync(cancellationToken);

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string? ownerUserId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        await ValidatePlanChangeTargetAsync(subscription, targetProductHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, subscription.ProductHandle, targetProductHandle, timing, cancellationToken);
    }

    public async Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string? ownerUserId, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(confirmedPreview, nameof(confirmedPreview));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        await ValidatePlanChangeTargetAsync(subscription, confirmedPreview.TargetProductHandle, cancellationToken);

        // UC3: never silently apply a different amount than the one the customer confirmed — re-derive
        // a fresh preview and reject the commit if pricing has drifted since the customer saw it.
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(
            subscriptionId, subscription.ProductHandle, confirmedPreview.TargetProductHandle, confirmedPreview.Timing, cancellationToken);
        if (!freshPreview.HasSamePricingAs(confirmedPreview))
        {
            throw new StalePlanChangePreviewException(subscriptionId);
        }

        var previousProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, confirmedPreview.TargetProductHandle, confirmedPreview.Timing, cancellationToken);

        await PublishBestEffort(new SubscriptionPlanChanged(subscriptionId, previousProductHandle, updated.ProductHandle), cancellationToken);

        return updated;
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!subscription.CanPause)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "pause");
        }

        return await ApplyTransitionAsync(subscription, () => _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!subscription.CanResume)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "resume");
        }

        return await ApplyTransitionAsync(subscription, () => _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<Subscription> CancelAsync(int subscriptionId, string? ownerUserId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!subscription.CanCancel)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "cancel");
        }

        return await ApplyTransitionAsync(subscription, () => _billingClient.CancelSubscriptionAsync(subscriptionId, timing, reason, cancellationToken), cancellationToken);
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!subscription.CanReactivate)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "reactivate");
        }

        return await ApplyTransitionAsync(subscription, () => _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    private async Task<Subscription> GetOwnedSubscriptionAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (ownerUserId is not null && subscription.OwnerReference != ownerUserId)
        {
            // Deliberately the same exception as "doesn't exist" (see SubscriptionNotFoundException) —
            // a customer probing another user's subscription id must not learn it exists.
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private async Task ValidatePlanChangeTargetAsync(Subscription subscription, string targetProductHandle, CancellationToken cancellationToken)
    {
        if (!subscription.CanChangePlan)
        {
            throw new InvalidSubscriptionStateException(subscription.Id, subscription.State, "change the plan of");
        }

        if (targetProductHandle == subscription.ProductHandle)
        {
            throw new ArgumentException("The target plan is the same as the subscription's current plan.", nameof(targetProductHandle));
        }

        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => p.Handle == targetProductHandle))
        {
            throw new BillingConfigurationException(
                $"Target product handle '{targetProductHandle}' does not resolve to a plan in the billing provider; check the seed (UC0).");
        }
    }

    private async Task<Subscription> ApplyTransitionAsync(Subscription subscription, Func<Task<Subscription>> transition, CancellationToken cancellationToken)
    {
        var previousState = subscription.State;
        Subscription updated;
        try
        {
            updated = await transition();
        }
        catch (BillingProviderException ex)
        {
            // UC4: the provider rejected a transition the local pre-flight check allowed — state
            // drifted out-of-band (dunning, or an admin action in the Maxio UI; there are no
            // webhooks, plan.md §7). Treat the provider's state as truth and surface the conflict.
            _logger.LogWarning(
                "Billing provider rejected a state transition for subscription {SubscriptionId}: {Message}. Refreshing local view.",
                subscription.Id, ex.Message);
            var refreshed = await _billingClient.GetSubscriptionAsync(subscription.Id, cancellationToken);
            throw new InvalidSubscriptionStateException(subscription.Id, refreshed.State, "complete the requested transition on");
        }

        await PublishBestEffort(new SubscriptionStateChanged(subscription.Id, previousState, updated.State), cancellationToken);

        return updated;
    }

    private async Task PublishBestEffort(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort in-process eventing (plan.md §2.5): a handler failure must never roll back
            // or fail an already-successful provider call.
            _logger.LogWarning("Failed to publish {NotificationType}: {Message}", notification.GetType().Name, ex.Message);
        }
    }
}
