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
    private static readonly BillingSubscriptionState[] ActiveLikeStates =
    {
        BillingSubscriptionState.Active, BillingSubscriptionState.Trialing
    };

    private static readonly BillingSubscriptionState[] PausedLikeStates =
    {
        BillingSubscriptionState.Paused, BillingSubscriptionState.OnHold
    };

    private static readonly BillingSubscriptionState[] ReactivatableStates =
    {
        BillingSubscriptionState.Canceled, BillingSubscriptionState.TrialEnded, BillingSubscriptionState.Unpaid
    };

    private static readonly BillingSubscriptionState[] TerminalStates =
    {
        BillingSubscriptionState.Canceled, BillingSubscriptionState.Expired, BillingSubscriptionState.FailedToCreate
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

    public Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.GetPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userId, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var (firstName, lastName) = DeriveCustomerName(userId);
        var customer = await _billingClient.EnsureCustomerAsync(userId, userId, firstName, lastName, cancellationToken);

        var existing = await FindActiveSubscriptionAsync(customer.Id, cancellationToken);
        if (existing != null)
        {
            return ToSubscription(userId, existing);
        }

        var created = await _billingClient.CreateSubscriptionAsync(userId, productHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userId, created.Id, productHandle), cancellationToken);

        return ToSubscription(userId, created);
    }

    public async Task<Subscription?> GetMySubscriptionAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var customer = await _billingClient.FindCustomerAsync(userId, cancellationToken);
        if (customer == null)
        {
            return null;
        }

        var active = await FindActiveSubscriptionAsync(customer.Id, cancellationToken);
        return active == null ? null : ToSubscription(userId, active);
    }

    public async Task RecordOrderPlacedUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _billingClient.FindCustomerAsync(userId, cancellationToken);
            if (customer == null)
            {
                return;
            }

            var active = await FindActiveSubscriptionAsync(customer.Id, cancellationToken);
            if (active == null)
            {
                return;
            }

            await _billingClient.RecordUsageAsync(active.Id, 1, "Order placed", cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Failed to record order-placed usage for user {0}: {1}", userId, ex.Message);
        }
    }

    public async Task<UsageResult> RecordUsageAsync(string userId, bool isAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Usage quantity must be greater than zero.", nameof(quantity));
        }

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);

        if (!ActiveLikeStates.Contains(subscription.State))
        {
            throw new IllegalSubscriptionTransitionException($"Subscription {subscriptionId} is not active (state: {subscription.State}); usage cannot be recorded.");
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, bool isAdmin, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);
        EnsureDifferentPlan(subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);
    }

    public async Task<Subscription> CommitPlanChangeAsync(string userId, bool isAdmin, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);
        EnsureDifferentPlan(subscription, targetProductHandle);

        if (!ActiveLikeStates.Contains(subscription.State))
        {
            throw new IllegalSubscriptionTransitionException($"Subscription {subscriptionId} cannot change plans from state {subscription.State}; reactivate it first.");
        }

        var oldProductHandle = subscription.ProductHandle ?? string.Empty;
        var updated = await _billingClient.ChangePlanAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionPlanChanged(userId, subscriptionId, oldProductHandle, targetProductHandle), cancellationToken);

        return ToSubscription(userId, updated);
    }

    public async Task<Subscription> PauseAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);

        if (PausedLikeStates.Contains(subscription.State) || TerminalStates.Contains(subscription.State))
        {
            throw new IllegalSubscriptionTransitionException($"Cannot pause subscription {subscriptionId} from state {subscription.State}.");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(userId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return ToSubscription(userId, updated);
    }

    public async Task<Subscription> ResumeAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);

        if (!PausedLikeStates.Contains(subscription.State))
        {
            throw new IllegalSubscriptionTransitionException($"Cannot resume subscription {subscriptionId} from state {subscription.State}; it is not paused.");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(userId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return ToSubscription(userId, updated);
    }

    public async Task<Subscription> CancelAsync(string userId, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);

        if (subscription.State == BillingSubscriptionState.Canceled || subscription.State == BillingSubscriptionState.Expired)
        {
            throw new IllegalSubscriptionTransitionException($"Cannot cancel subscription {subscriptionId}; it is already {subscription.State}.");
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChangeAsync(userId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return ToSubscription(userId, updated);
    }

    public async Task<Subscription> ReactivateAsync(string userId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureOwnership(userId, isAdmin, subscription);

        if (!ReactivatableStates.Contains(subscription.State))
        {
            throw new IllegalSubscriptionTransitionException($"Cannot reactivate subscription {subscriptionId} from state {subscription.State}.");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(userId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return ToSubscription(userId, updated);
    }

    private async Task<BillingSubscription?> FindActiveSubscriptionAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await _billingClient.GetSubscriptionsForCustomerAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => ActiveLikeStates.Contains(s.State));
    }

    private static void EnsureOwnership(string userId, bool isAdmin, BillingSubscription subscription)
    {
        if (isAdmin)
        {
            return;
        }

        if (!string.Equals(subscription.CustomerReference, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionAccessDeniedException(subscription.Id);
        }
    }

    private static void EnsureDifferentPlan(BillingSubscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new IllegalSubscriptionTransitionException("The target plan is the same as the current plan.");
        }
    }

    private async Task PublishStateChangeAsync(string userId, int subscriptionId, BillingSubscriptionState oldState, BillingSubscriptionState newState, CancellationToken cancellationToken) =>
        await PublishBestEffortAsync(new SubscriptionStateChanged(userId, subscriptionId, oldState, newState), cancellationToken);

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

    private static Subscription ToSubscription(string userId, BillingSubscription s) => new(
        userId,
        s.Id,
        s.CustomerId,
        s.CustomerReference,
        s.ProductHandle ?? string.Empty,
        s.ProductName,
        s.PriceInCents,
        s.State,
        s.NextBillingDate,
        s.CurrentPeriodEndsAt,
        s.DelayedCancelAt);

    private static (string FirstName, string LastName) DeriveCustomerName(string userId)
    {
        var atIndex = userId.IndexOf('@');
        var firstName = atIndex > 0 ? userId[..atIndex] : userId;
        return (firstName, "eShopOnWeb Customer");
    }
}
