using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Orchestrates the subscription use cases: validates the request, drives the billing provider
/// through <see cref="IBillingClient"/>, and announces the outcome through in-process notifications.
/// Mirrors <see cref="OrderService"/> — no provider or transport type appears here.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Fail on a stale/unknown handle before touching the customer record, so a re-seeded sandbox
        // surfaces as a configuration error rather than an enrollment against a guessed plan.
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not resolve in the billing provider. Verify the seeded product handles and the configured plan handles.");
        }

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException($"Plan '{planHandle}' is archived and cannot be subscribed to.");
        }

        // Repeated subscribe (double click, retried call) must never create a second enrollment.
        var existing = await GetActiveSubscriptionAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscribe requested for {Reference} but subscription {SubscriptionId} is already active; returning the existing subscription.",
                subscriber.Reference,
                existing.Id);
            return existing;
        }

        // Idempotent on the user reference: safe to retry after a partial failure.
        var customer = await _billingClient.EnsureCustomerAsync(subscriber, cancellationToken);

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionActivated(subscriber.Reference, subscription),
            cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        return _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
    }

    public async Task<BillingSubscription?> GetActiveSubscriptionAsync(
        string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsActive);
    }

    public async Task<UsageRecordResult> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        // Reject invalid input before anything is sent to the provider.
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero, but was {quantity.ToString(CultureInfo.InvariantCulture)}.");
        }

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} does not exist.");

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage cannot be recorded against subscription {subscriptionId} because it is {subscription.State}.");
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<UsageRecordResult> RecordUsageForUserAsync(
        string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscription = await GetActiveSubscriptionAsync(userReference, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException(
                $"'{userReference}' has no active subscription to record usage against.");

        return await RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);
    }

    public Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => _billingClient.GetPeriodToDateUsageAsync(subscriptionId, cancellationToken);

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        EnsurePlanChangeIsAllowed(subscription, targetPlanHandle);
        await EnsureTargetPlanResolvesAsync(targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<BillingSubscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewFingerprint,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));
        Guard.Against.NullOrWhiteSpace(previewFingerprint, nameof(previewFingerprint));

        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        EnsurePlanChangeIsAllowed(subscription, targetPlanHandle);
        await EnsureTargetPlanResolvesAsync(targetPlanHandle, cancellationToken);

        // Re-quote and compare: the customer is never charged an amount other than the one confirmed.
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(
            subscriptionId, targetPlanHandle, timing, cancellationToken);

        if (!string.Equals(freshPreview.Fingerprint, previewFingerprint, StringComparison.Ordinal))
        {
            throw new StalePlanChangePreviewException(
                $"The proration quoted for subscription {subscriptionId} changed before the plan change was confirmed. Request a fresh preview and confirm again.");
        }

        var previousPlanHandle = subscription.PlanHandle;
        var updated = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        var effectiveAt = timing == PlanChangeTiming.Immediate
            ? DateTimeOffset.UtcNow
            : updated.CurrentPeriodEndsAt ?? updated.NextAssessmentAt;

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(
                updated.CustomerReference ?? subscription.CustomerReference ?? string.Empty,
                updated,
                previousPlanHandle,
                targetPlanHandle,
                timing,
                freshPreview.PaymentDue,
                effectiveAt),
            cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> ApplyLifecycleActionAsync(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming = CancellationTiming.Immediate,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        var previousState = subscription.State;

        EnsureTransitionIsLegal(subscription, action);

        BillingSubscription updated;
        try
        {
            updated = action switch
            {
                SubscriptionLifecycleAction.Pause =>
                    await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
                SubscriptionLifecycleAction.Resume =>
                    await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
                SubscriptionLifecycleAction.Cancel =>
                    await _billingClient.CancelSubscriptionAsync(subscriptionId, cancellationTiming, reason, cancellationToken),
                SubscriptionLifecycleAction.Reactivate =>
                    await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
                _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
            };
        }
        catch (BillingProviderException ex)
        {
            // The provider rejected a transition the local check allowed, which means the state
            // drifted out of band. The provider is the system of record: refresh and surface the
            // conflict rather than reporting our stale view.
            var refreshed = await TryRefreshStateAsync(subscriptionId, cancellationToken);
            if (refreshed is not null && refreshed.State != previousState)
            {
                throw new BillingProviderException(
                    $"Cannot {action} subscription {subscriptionId}: the billing provider reports it is {refreshed.State}, not {previousState}. {ex.Message}",
                    ex);
            }

            throw;
        }

        var effectiveAt = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
            ? updated.DelayedCancelAt ?? updated.CurrentPeriodEndsAt
            : DateTimeOffset.UtcNow;

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(
                updated.CustomerReference ?? subscription.CustomerReference ?? string.Empty,
                updated,
                previousState,
                updated.State,
                action,
                effectiveAt),
            cancellationToken);

        return updated;
    }

    private async Task<BillingSubscription> RequireSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
        => await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
           ?? throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} does not exist.");

    private async Task EnsureTargetPlanResolvesAsync(string targetPlanHandle, CancellationToken cancellationToken)
    {
        var target = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (target is null)
        {
            throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve in the billing provider. Verify the seeded product handles and the configured plan handles.");
        }

        if (target.IsArchived)
        {
            throw new BillingConfigurationException($"Target plan '{targetPlanHandle}' is archived and cannot be moved to.");
        }
    }

    private static void EnsurePlanChangeIsAllowed(BillingSubscription subscription, string targetPlanHandle)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is {subscription.State} and cannot change plan. Reactivate it first.");
        }
    }

    private static void EnsureTransitionIsLegal(BillingSubscription subscription, SubscriptionLifecycleAction action)
    {
        var state = subscription.State;

        switch (action)
        {
            case SubscriptionLifecycleAction.Pause when state is not (SubscriptionState.Active or SubscriptionState.Trialing):
                throw Illegal(subscription, action, "only an active or trialing subscription can be paused");

            case SubscriptionLifecycleAction.Resume when state is not SubscriptionState.Paused:
                throw Illegal(subscription, action, "only a paused subscription can be resumed");

            case SubscriptionLifecycleAction.Cancel when state is SubscriptionState.Canceled or SubscriptionState.Expired:
                throw Illegal(subscription, action, "the subscription is already cancelled");

            case SubscriptionLifecycleAction.Reactivate when state is not (SubscriptionState.Canceled or SubscriptionState.Expired):
                throw Illegal(subscription, action, "only a cancelled or expired subscription can be reactivated");

            default:
                return;
        }
    }

    private static InvalidSubscriptionOperationException Illegal(
        BillingSubscription subscription,
        SubscriptionLifecycleAction action,
        string because)
        => new($"Cannot {action} subscription {subscription.Id}: it is {subscription.State} and {because}.");

    private async Task<BillingSubscription?> TryRefreshStateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not re-read subscription {SubscriptionId} after a failed transition: {Message}",
                subscriptionId,
                ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Publishes a lifecycle notification without letting a handler failure undo work the provider
    /// has already committed. Eventing is in-process and best-effort by design — there is no broker
    /// and no outbox, so the only guarantee is that registered handlers are invoked.
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "In-process publication of {Notification} failed after the billing operation succeeded: {Message}",
                notification.GetType().Name,
                ex.Message);
        }
    }
}
