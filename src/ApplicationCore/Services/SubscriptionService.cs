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

public class SubscriptionService : ISubscriptionService
{
    private static readonly SubscriptionStatus[] ActiveLikeStatuses =
    {
        SubscriptionStatus.Active,
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Assessing,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.SoftFailure,
        SubscriptionStatus.Unpaid
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

    public async Task<Subscription> SubscribeAsync(string buyerId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingProviderException($"Configured product handle '{productHandle}' does not resolve. Verify the sandbox seed (UC0).");
        }

        await _billingClient.EnsureCustomerAsync(buyerId, email, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(buyerId, cancellationToken);
        var activeSubscription = existingSubscriptions.FirstOrDefault(s => ActiveLikeStatuses.Contains(s.Status));
        if (activeSubscription is not null)
        {
            _logger.LogInformation("Buyer {0} already has active subscription {1}; skipping duplicate enrollment", buyerId, activeSubscription.Id);
            return activeSubscription;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(buyerId, productHandle, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionActivated(buyerId, subscription.Id, subscription.ProductHandle, subscription.ProductName, subscription.PriceInCents),
            cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyList<Subscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return _billingClient.ListCustomerSubscriptionsAsync(buyerId, cancellationToken);
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);
        return subscription;
    }

    public async Task<UsageSummary> RecordUsageAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);

        if (subscription.Status != SubscriptionStatus.Active)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status, "record usage");
        }

        await _billingClient.EnsureMeteredComponentConfiguredAsync(cancellationToken);

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task RecordUsageForOrderAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(buyerId, cancellationToken);
            var activeSubscription = subscriptions.FirstOrDefault(s => s.Status == SubscriptionStatus.Active);
            if (activeSubscription is null)
            {
                _logger.LogInformation("Buyer {0} placed an order but has no active subscription; skipping automatic usage", buyerId);
                return;
            }

            await _billingClient.EnsureMeteredComponentConfiguredAsync(cancellationToken);
            await _billingClient.RecordUsageAsync(activeSubscription.Id, 1, "Order placed", cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Automatic usage recording failed for buyer {0}: {1}", buyerId, ex.Message);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);
        await EnsurePlanChangeIsLegalAsync(subscription, targetProductHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, subscription.ProductHandle, targetProductHandle, timing, cancellationToken);
    }

    public async Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, string targetProductHandle, PlanChangeTiming timing, long previewedAmountInCents, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);
        await EnsurePlanChangeIsLegalAsync(subscription, targetProductHandle, cancellationToken);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, subscription.ProductHandle, targetProductHandle, timing, cancellationToken);
        if (freshPreview.ComparableAmountInCents != previewedAmountInCents)
        {
            throw new StalePlanChangePreviewException(subscriptionId);
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = timing == PlanChangeTiming.Now
            ? await _billingClient.ApplyPlanChangeNowAsync(subscriptionId, targetProductHandle, cancellationToken)
            : await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetProductHandle, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(requestingBuyerId, subscriptionId, oldProductHandle, targetProductHandle, freshPreview.ComparableAmountInCents, freshPreview.EffectiveAt),
            cancellationToken);

        return updated;
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);

        if (subscription.Status != SubscriptionStatus.Active)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status, "pause");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(requestingBuyerId, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);

        if (subscription.Status != SubscriptionStatus.OnHold && subscription.Status != SubscriptionStatus.Paused)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status, "resume");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(requestingBuyerId, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);

        if (subscription.Status == SubscriptionStatus.Canceled || subscription.Status == SubscriptionStatus.Expired)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status, "cancel");
        }

        if (timing == CancellationTiming.EndOfPeriod && subscription.CancelAtEndOfPeriod)
        {
            _logger.LogInformation("Subscription {0} is already pending end-of-period cancellation; returning current state", subscriptionId);
            return subscription;
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, timing == CancellationTiming.EndOfPeriod, reason, cancellationToken);
        await PublishStateChangeAsync(requestingBuyerId, subscription, updated, cancellationToken);
        return updated;
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(subscription, requestingBuyerId, isAdmin);

        if (subscription.Status != SubscriptionStatus.Canceled && subscription.Status != SubscriptionStatus.TrialEnded)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status, "reactivate");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(requestingBuyerId, subscription, updated, cancellationToken);
        return updated;
    }

    private async Task EnsurePlanChangeIsLegalAsync(Subscription subscription, string targetProductHandle, CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Subscription {subscription.Id} is already on plan '{targetProductHandle}'.", nameof(targetProductHandle));
        }

        if (subscription.Status == SubscriptionStatus.Canceled || subscription.Status == SubscriptionStatus.Expired)
        {
            throw new InvalidSubscriptionStateException(subscription.Id, subscription.Status, "change plan");
        }

        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, targetProductHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingProviderException($"Configured product handle '{targetProductHandle}' does not resolve. Verify the sandbox seed (UC0).");
        }
    }

    private static void EnsureOwnership(Subscription subscription, string requestingBuyerId, bool isAdmin)
    {
        if (!isAdmin && !string.Equals(subscription.CustomerReference, requestingBuyerId, StringComparison.Ordinal))
        {
            throw new UnauthorizedSubscriptionAccessException(subscription.Id);
        }
    }

    private async Task PublishStateChangeAsync(string buyerId, Subscription before, Subscription after, CancellationToken cancellationToken)
    {
        await PublishBestEffortAsync(new SubscriptionStateChanged(buyerId, after.Id, before.Status, after.Status), cancellationToken);
    }

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish {0}: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
