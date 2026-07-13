using System;
using System.Collections.Generic;
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
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<SubscriptionDetails> SubscribeAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var existing = await _billingClient.FindActiveSubscriptionAsync(customerReference, cancellationToken);
        if (existing != null)
        {
            // Duplicate subscribe (double-click, repeated call): return the existing enrollment
            // rather than creating a second one (UC1 failure scenario).
            return existing;
        }

        var created = await _billingClient.CreateSubscriptionAsync(customerReference, customerEmail, productHandle, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionActivated(customerReference, created.Id, created.ProductHandle),
            cancellationToken);

        return created;
    }

    public Task<SubscriptionDetails?> GetActiveSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        return _billingClient.FindActiveSubscriptionAsync(customerReference, cancellationToken);
    }

    public Task<SubscriptionDetails?> GetCurrentSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        return _billingClient.GetCurrentSubscriptionAsync(customerReference, cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        return _billingClient.ListSubscriptionsAsync(customerReference, cancellationToken);
    }

    public Task<SubscriptionDetails> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        return _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public async Task<ComponentUsageStatus> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!subscription.IsActiveOrTrialing)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "record usage");
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public Task<ComponentUsageStatus> GetUsageStatusAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        return _billingClient.GetComponentUsageStatusAsync(subscriptionId, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsurePlanChangeIsLegal(subscriptionId, subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);
    }

    public async Task<SubscriptionDetails> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.Null(confirmedPreview, nameof(confirmedPreview));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsurePlanChangeIsLegal(subscriptionId, subscription, targetProductHandle);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, confirmedPreview.ApplyImmediately, cancellationToken);
        if (freshPreview.ApplyImmediately != confirmedPreview.ApplyImmediately ||
            freshPreview.ProratedAdjustmentInCents != confirmedPreview.ProratedAdjustmentInCents ||
            freshPreview.ChargeInCents != confirmedPreview.ChargeInCents ||
            freshPreview.PaymentDueInCents != confirmedPreview.PaymentDueInCents ||
            freshPreview.CreditAppliedInCents != confirmedPreview.CreditAppliedInCents)
        {
            // Never silently apply a different amount than the one shown (UC3).
            throw new StalePlanChangePreviewException();
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetProductHandle, confirmedPreview.ApplyImmediately, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(subscription.CustomerReference, subscriptionId, oldProductHandle, updated.ProductHandle),
            cancellationToken);

        return updated;
    }

    public Task<SubscriptionDetails> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "pause",
            current => current.State is SubscriptionState.Active or SubscriptionState.Trialing,
            (client, id, ct) => client.PauseSubscriptionAsync(id, ct),
            cancellationToken);

    public Task<SubscriptionDetails> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "resume",
            current => current.State == SubscriptionState.OnHold,
            (client, id, ct) => client.ResumeSubscriptionAsync(id, ct),
            cancellationToken);

    public Task<SubscriptionDetails> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            endOfPeriod ? "cancel at end of period" : "cancel",
            current => current.State != SubscriptionState.Canceled,
            (client, id, ct) => client.CancelSubscriptionAsync(id, endOfPeriod, reason, ct),
            cancellationToken);

    public Task<SubscriptionDetails> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "reactivate",
            current => current.State is SubscriptionState.Canceled or SubscriptionState.TrialEnded or SubscriptionState.PastDue or SubscriptionState.Unpaid,
            (client, id, ct) => client.ReactivateSubscriptionAsync(id, ct),
            cancellationToken);

    private async Task<SubscriptionDetails> TransitionAsync(
        int subscriptionId,
        string action,
        Func<SubscriptionDetails, bool> isLegalFromCurrentState,
        Func<IBillingClient, int, CancellationToken, Task<SubscriptionDetails>> performTransition,
        CancellationToken cancellationToken)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var current = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!isLegalFromCurrentState(current))
        {
            throw new InvalidSubscriptionStateException(subscriptionId, current.State, action);
        }

        var updated = await performTransition(_billingClient, subscriptionId, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(current.CustomerReference, subscriptionId, current.State, updated.State),
            cancellationToken);

        return updated;
    }

    private static void EnsurePlanChangeIsLegal(int subscriptionId, SubscriptionDetails subscription, string targetProductHandle)
    {
        if (!subscription.IsActiveOrTrialing)
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "change plan");
        }

        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderException($"Subscription {subscriptionId} is already on plan '{targetProductHandle}'.");
        }
    }

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort, in-process eventing (plan.md §2.5): the provider call already
            // succeeded, so a handler failure is logged and swallowed, never rolled back.
            _logger.LogWarning("Failed to publish {0}: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
