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
/// Orchestrates the subscription use cases (mirrors <see cref="OrderService"/>): validates input, drives
/// the billing client, enforces lifecycle rules, and publishes best-effort in-process notifications
/// (plan.md §2.5). This class never references the billing provider's SDK directly — only
/// <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string ActiveState = "active";
    private const string PausedState = "paused";
    private const string OnHoldState = "on_hold";
    private const string CanceledState = "canceled";
    private const string ExpiredState = "expired";

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

    public async Task<Subscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var plan = await _billingClient.FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{productHandle}' is not configured on the billing provider. Re-run UC0 (seed the sandbox) and update configuration.");
        }

        var customer = await _billingClient.FindOrCreateCustomerAsync(userId, email, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existingActive = existingSubscriptions.FirstOrDefault(s => IsState(s.State, ActiveState));
        if (existingActive is not null)
        {
            if (!string.Equals(existingActive.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase))
            {
                // A genuinely different plan is requested while already active on another — this is not
                // the "duplicate subscribe" case (plan.md UC1), it's a plan change (UC3). Reject rather
                // than silently returning the wrong (existing) subscription.
                throw new ArgumentException(
                    $"You already have an active subscription on plan '{existingActive.ProductHandle}'. Use plan change (UC3) to move to '{productHandle}'.",
                    nameof(productHandle));
            }

            // Idempotent double-subscribe on the SAME plan (plan.md UC1): never create a second enrollment.
            return Map(userId, existingActive);
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);

        await PublishBestEffort(new SubscriptionActivated(userId, created.Id, created.ProductHandle), cancellationToken);

        return Map(userId, created);
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var customer = await _billingClient.FindCustomerAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => Map(userId, s)).ToList();
    }

    public async Task<BillingUsageReading> RecordUsageAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(actingUserId, nameof(actingUserId));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        await _billingClient.EnsureMeteredComponentConfiguredAsync(cancellationToken);

        var subscription = await GetOwnedSubscriptionAsync(actingUserId, actingAsAdmin, subscriptionId, cancellationToken);
        if (!IsState(subscription.State, ActiveState))
        {
            throw new InvalidSubscriptionTransitionException("record usage on", subscription.State);
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        var subscription = await ValidatePlanChangeRequestAsync(userId, subscriptionId, targetProductHandle, cancellationToken);
        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);
    }

    public async Task<Subscription> CommitPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, string stalenessToken, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(stalenessToken, nameof(stalenessToken));

        var subscription = await ValidatePlanChangeRequestAsync(userId, subscriptionId, targetProductHandle, cancellationToken);

        var currentToken = BillingStalenessToken.From(subscription);
        if (!string.Equals(currentToken, stalenessToken, StringComparison.Ordinal))
        {
            throw new PlanChangePreviewStaleException();
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);

        await PublishBestEffort(new SubscriptionPlanChanged(userId, subscriptionId, oldProductHandle, targetProductHandle, applyImmediately), cancellationToken);

        return Map(userId, updated);
    }

    public async Task<Subscription> PauseAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingUserId, actingAsAdmin, subscriptionId, cancellationToken);
        if (!IsState(subscription.State, ActiveState))
        {
            throw new InvalidSubscriptionTransitionException("pause", subscription.State);
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChanged(actingUserId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return Map(actingUserId, updated);
    }

    public async Task<Subscription> ResumeAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingUserId, actingAsAdmin, subscriptionId, cancellationToken);
        if (!IsState(subscription.State, PausedState) && !IsState(subscription.State, OnHoldState))
        {
            throw new InvalidSubscriptionTransitionException("resume", subscription.State);
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChanged(actingUserId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return Map(actingUserId, updated);
    }

    public async Task<Subscription> CancelAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingUserId, actingAsAdmin, subscriptionId, cancellationToken);
        if (IsState(subscription.State, CanceledState) || IsState(subscription.State, ExpiredState))
        {
            throw new InvalidSubscriptionTransitionException("cancel", subscription.State);
        }

        if (endOfPeriod && subscription.CancelAtEndOfPeriod)
        {
            // Already pending cancellation out-of-band: surface the provider's outcome rather than
            // reporting the request as newly applied (plan.md UC4).
            return Map(actingUserId, subscription);
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChanged(actingUserId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return Map(actingUserId, updated);
    }

    public async Task<Subscription> ReactivateAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingUserId, actingAsAdmin, subscriptionId, cancellationToken);
        if (!IsState(subscription.State, CanceledState) && !IsState(subscription.State, ExpiredState))
        {
            throw new InvalidSubscriptionTransitionException("reactivate", subscription.State);
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChanged(actingUserId, subscriptionId, subscription.State, updated.State, cancellationToken);
        return Map(actingUserId, updated);
    }

    private async Task<BillingSubscription> ValidatePlanChangeRequestAsync(string userId, int subscriptionId, string targetProductHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetOwnedSubscriptionAsync(userId, actingAsAdmin: false, subscriptionId, cancellationToken);

        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Subscription {subscriptionId} is already on plan '{targetProductHandle}'.", nameof(targetProductHandle));
        }

        if (IsState(subscription.State, CanceledState) || IsState(subscription.State, ExpiredState))
        {
            throw new InvalidSubscriptionTransitionException("change the plan of", subscription.State);
        }

        var targetPlan = await _billingClient.FindPlanAsync(targetProductHandle, cancellationToken);
        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{targetProductHandle}' is not configured on the billing provider. Re-run UC0 (seed the sandbox) and update configuration.");
        }

        return subscription;
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (!actingAsAdmin)
        {
            var customer = await _billingClient.FindCustomerAsync(actingUserId, cancellationToken);
            if (customer is null || customer.Id != subscription.CustomerId)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }
        }

        return subscription;
    }

    private async Task PublishStateChanged(string userId, int subscriptionId, string oldState, string newState, CancellationToken cancellationToken)
    {
        if (string.Equals(oldState, newState, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await PublishBestEffort(new SubscriptionStateChanged(userId, subscriptionId, oldState, newState), cancellationToken);
    }

    private async Task PublishBestEffort(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort, in-process eventing (plan.md §2.5): the subscription state already stands;
            // a handler failure is logged, never rolled back or rethrown.
            _logger.LogWarning("Failed to publish {0}: {1}", notification.GetType().Name, ex.Message);
        }
    }

    private static bool IsState(string state, string expected) =>
        string.Equals(state, expected, StringComparison.OrdinalIgnoreCase);

    private static Subscription Map(string userId, BillingSubscription s) =>
        new(s.Id, userId, s.CustomerId, s.ProductHandle, s.ProductId, s.State, s.CancelAtEndOfPeriod, s.CurrentPeriodEndsAt);
}
