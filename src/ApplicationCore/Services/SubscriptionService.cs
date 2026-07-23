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
/// Orchestrates the subscription use cases (§4.2), mirroring <see cref="OrderService"/>: validate,
/// call the billing client, publish the in-process notification.
/// </summary>
/// <remarks>
/// The userId-to-subscription mapping is stateless (§8): every read resolves the provider-side
/// customer from the eShopOnWeb user reference, so the provider stays the single system of record
/// and repeated calls are idempotent.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: an unresolvable handle is a seeding problem (UC0).
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not exist on the billing provider. Re-seed the sandbox (UC0) or correct the configured plan handles.");

        var customer = await _billingClient.EnsureCustomerAsync(
            new EnsureCustomerRequest(buyerId, buyerId), cancellationToken);

        // A duplicate subscribe (double-click, repeated call) must return the existing enrollment
        // rather than create a second one.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var active = existing.FirstOrDefault(s => s.IsActive);
        if (active is not null)
        {
            _logger.LogInformation(
                "Subscribe skipped for {0}: subscription {1} is already active on plan {2}.",
                buyerId, active.Id, active.ProductHandle);
            return new Subscription(buyerId, active);
        }

        var created = await _billingClient.CreateSubscriptionAsync(
            new CreateSubscriptionRequest(customer.Id, plan.Handle), cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionActivated(buyerId, created.Id, created.ProductHandle, created.ProductName,
                created.ProductPriceInCents, created.NextAssessmentAt),
            cancellationToken);

        return new Subscription(buyerId, created);
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var customer = await _billingClient.FindCustomerByReferenceAsync(buyerId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => new Subscription(buyerId, s)).ToList();
    }

    public async Task<Subscription?> GetSubscriptionForUserAsync(string buyerId, long subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var subscriptions = await GetSubscriptionsForUserAsync(buyerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.ProviderSubscriptionId == subscriptionId);
    }

    public async Task<UsageRecordResult> RecordUsageAsync(string buyerId, int quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        GuardQuantity(quantity);

        var subscriptions = await GetSubscriptionsForUserAsync(buyerId, cancellationToken);
        var active = subscriptions.FirstOrDefault(s => s.IsActive)
            ?? throw new InvalidSubscriptionOperationException(
                $"'{buyerId}' has no active subscription, so usage cannot be recorded.");

        return await RecordUsageCoreAsync(active.ProviderSubscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<UsageRecordResult> RecordUsageForSubscriptionAsync(long subscriptionId, int quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        GuardQuantity(quantity);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} does not exist.");

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is {subscription.State} and cannot accrue usage.");
        }

        return await RecordUsageCoreAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<UsageRecordResult> GetUsageSummaryAsync(long subscriptionId, CancellationToken cancellationToken = default)
    {
        var component = await _billingClient.GetUsageComponentAsync(cancellationToken);
        var total = await ReadPeriodToDateAsync(subscriptionId, component.Id, cancellationToken);

        return new UsageRecordResult(usageId: 0, subscriptionId, component.Handle, quantity: 0,
            memo: null, periodToDateUnits: total, unitPrice: component.UnitPrice);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string buyerId,
        long subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetChangeableSubscriptionAsync(buyerId, subscriptionId, targetPlanHandle, cancellationToken);
        return await _billingClient.PreviewPlanChangeAsync(subscription.ProviderSubscriptionId, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(string buyerId,
        long subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string confirmedPreviewFingerprint,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(confirmedPreviewFingerprint, nameof(confirmedPreviewFingerprint));

        var subscription = await GetChangeableSubscriptionAsync(buyerId, subscriptionId, targetPlanHandle, cancellationToken);
        var previousPlanHandle = subscription.PlanHandle;

        // Re-price immediately before committing: if the provider's numbers moved since the customer
        // confirmed, reject and require a fresh preview rather than charging a different amount.
        var current = await _billingClient.PreviewPlanChangeAsync(subscription.ProviderSubscriptionId, targetPlanHandle, timing, cancellationToken);
        if (!string.Equals(current.Fingerprint, confirmedPreviewFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidSubscriptionOperationException(
                "The previewed cost of this plan change is no longer current. Review the refreshed preview and confirm again.");
        }

        var updated = await ApplyProviderTransitionAsync(
            subscription,
            () => _billingClient.ChangePlanAsync(subscription.ProviderSubscriptionId, targetPlanHandle, timing, cancellationToken),
            cancellationToken);

        var effectiveAt = timing == PlanChangeTiming.Immediate
            ? updated.CurrentPeriodStartsAt
            : updated.CurrentPeriodEndsAt;

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(buyerId, updated.Id, previousPlanHandle, targetPlanHandle, timing,
                current.PaymentDueInCents, effectiveAt),
            cancellationToken);

        return new Subscription(buyerId, updated);
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(string buyerId,
        long subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming = CancellationTiming.Immediate,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var subscription = await GetSubscriptionForUserAsync(buyerId, subscriptionId, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} does not belong to '{buyerId}'.");

        var previousState = subscription.State;
        GuardTransition(action, previousState);

        var updated = await ApplyProviderTransitionAsync(
            subscription,
            () => action switch
            {
                SubscriptionLifecycleAction.Pause => _billingClient.PauseAsync(subscriptionId, cancellationToken),
                SubscriptionLifecycleAction.Resume => _billingClient.ResumeAsync(subscriptionId, cancellationToken),
                SubscriptionLifecycleAction.Cancel => _billingClient.CancelAsync(subscriptionId, cancellationTiming, reason, cancellationToken),
                SubscriptionLifecycleAction.Reactivate => _billingClient.ReactivateAsync(subscriptionId, cancellationToken),
                _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
            },
            cancellationToken);

        // An end-of-period cancel takes effect at the period boundary, not now.
        var effectiveAt = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
            ? updated.DelayedCancelAt ?? updated.CurrentPeriodEndsAt
            : DateTimeOffset.UtcNow;

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(buyerId, updated.Id, previousState, updated.State, action.ToString(), effectiveAt, reason),
            cancellationToken);

        return new Subscription(buyerId, updated);
    }

    private async Task<UsageRecordResult> RecordUsageCoreAsync(long subscriptionId, int quantity, string? memo, CancellationToken cancellationToken)
    {
        // Refuses to record against a component that is missing or not metered (UC2 precondition).
        var component = await _billingClient.GetUsageComponentAsync(cancellationToken);

        var usageId = await _billingClient.RecordUsageAsync(
            new RecordUsageRequest(subscriptionId, component.Id, quantity, memo), cancellationToken);

        var total = await ReadPeriodToDateAsync(subscriptionId, component.Id, cancellationToken);

        return new UsageRecordResult(usageId, subscriptionId, component.Handle, quantity, memo, total, component.UnitPrice);
    }

    /// <summary>
    /// Reads the period-to-date total, tolerating failure. The usage is already recorded at this
    /// point, so a failed read-back must not fail the whole operation — the total is reported as
    /// unavailable instead (UC2 failure scenarios).
    /// </summary>
    private async Task<int?> ReadPeriodToDateAsync(long subscriptionId, long componentId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetPeriodToDateUnitsAsync(subscriptionId, componentId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Could not read the period-to-date usage total for subscription {0}: {1}. The recorded usage stands.",
                subscriptionId, ex.Message);
            return null;
        }
    }

    private async Task<Subscription> GetChangeableSubscriptionAsync(string buyerId, long subscriptionId, string targetPlanHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetSubscriptionForUserAsync(buyerId, subscriptionId, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} does not belong to '{buyerId}'.");

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is {subscription.State}; reactivate it before changing plan.");
        }

        _ = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Plan '{targetPlanHandle}' does not exist on the billing provider. Re-seed the sandbox (UC0) or correct the configured plan handles.");

        return subscription;
    }

    /// <summary>
    /// Runs a provider mutation, and when the provider rejects a transition the local check allowed
    /// (state drifted out-of-band — there are no webhooks, §7) re-reads the provider's state and
    /// surfaces that conflict, because the provider's state is the truth.
    /// </summary>
    private async Task<BillingSubscription> ApplyProviderTransitionAsync(Subscription subscription,
        Func<Task<BillingSubscription>> transition,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transition();
        }
        catch (BillingProviderException ex)
        {
            var refreshed = await TryRefreshAsync(subscription.ProviderSubscriptionId, cancellationToken);
            if (refreshed is null)
            {
                throw;
            }

            subscription.RefreshFrom(refreshed);
            throw new BillingProviderException(
                $"The billing provider rejected this change. Subscription {subscription.ProviderSubscriptionId} is currently {refreshed.State} on plan '{refreshed.ProductHandle}'. {ex.Message}",
                ex.StatusCode,
                ex.ProviderErrors,
                ex);
        }
    }

    private async Task<BillingSubscription?> TryRefreshAsync(long subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Could not re-read subscription {0} after a failed transition: {1}", subscriptionId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Rejects illegal transitions before any provider call, naming the current state and what is
    /// legal from it (UC4 failure scenarios).
    /// </summary>
    private static void GuardTransition(SubscriptionLifecycleAction action, SubscriptionState state)
    {
        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                state is SubscriptionState.Active or SubscriptionState.Trialing,
            SubscriptionLifecycleAction.Resume =>
                state is SubscriptionState.Paused,
            SubscriptionLifecycleAction.Cancel =>
                state is not (SubscriptionState.Canceled or SubscriptionState.Expired),
            SubscriptionLifecycleAction.Reactivate =>
                state is SubscriptionState.Canceled or SubscriptionState.Expired,
            _ => false
        };

        if (legal)
        {
            return;
        }

        var allowed = action switch
        {
            SubscriptionLifecycleAction.Pause => "an active or trialing subscription",
            SubscriptionLifecycleAction.Resume => "a paused subscription",
            SubscriptionLifecycleAction.Cancel => "a subscription that is not already cancelled or expired",
            SubscriptionLifecycleAction.Reactivate => "a cancelled or expired subscription",
            _ => "a supported lifecycle action"
        };

        throw new InvalidSubscriptionOperationException(
            $"Cannot {action.ToString().ToLowerInvariant()} a subscription that is {state}. This action requires {allowed}.");
    }

    private static void GuardQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero, but was {quantity}.");
        }
    }

    /// <summary>
    /// Publishes an in-process notification without letting a handler failure undo work the
    /// provider already committed. Eventing is best-effort by design (§2.5).
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
                "In-process publication of {0} failed: {1}. The billing change stands.",
                notification.GetType().Name, ex.Message);
        }
    }
}
