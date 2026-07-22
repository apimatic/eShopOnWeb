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
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    /// <summary>States in which a subscription is live and may accrue usage or change plan.</summary>
    private static readonly string[] LiveStates = { "active", "trialing", "assessing", "pending", "past_due", "soft_failure" };

    /// <summary>States a subscription can be reactivated out of.</summary>
    private static readonly string[] ReactivatableStates = { "canceled", "expired", "trial_ended", "unpaid" };

    private const string PausedState = "on_hold";

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly SubscriptionSettings _settings;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger,
        SubscriptionSettings settings)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
        _settings = settings;
    }

    public Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string? planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var handle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultProductHandle : planHandle;
        var plan = await ResolvePlanAsync(handle, cancellationToken);

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken)
            ?? await _billingClient.CreateCustomerAsync(userReference, userReference, cancellationToken);

        // A repeated subscribe (double-click, retry) must never create a second enrollment.
        var existing = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => IsLive(s.State));
        if (alreadyActive is not null)
        {
            return Subscription.FromBilling(userReference, alreadyActive);
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(userReference, created.Id, created.ProductHandle ?? plan.Handle, created.ProductPrice),
            cancellationToken);

        return Subscription.FromBilling(userReference, created);
    }

    public async Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => Subscription.FromBilling(userReference, s)).ToList();
    }

    public async Task<UsageReportResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var component = await EnsureMeteredComponentAsync(cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException($"Subscription {subscriptionId} was not found.");

        if (!IsLive(subscription.State))
        {
            throw new BillingValidationException(
                $"Subscription {subscriptionId} is '{subscription.State}' and cannot accrue usage; an active subscription is required.");
        }

        var recorded = await _billingClient.RecordUsageAsync(subscriptionId, component.Handle!, quantity, memo, cancellationToken);

        var result = new UsageReportResult
        {
            SubscriptionId = subscriptionId,
            ComponentHandle = component.Handle!,
            UsageRecordId = recorded.Id,
            QuantityRecorded = recorded.Quantity,
            Memo = recorded.Memo ?? memo
        };

        // The usage stands even if the read-back fails - report it as unavailable rather than
        // failing the whole operation and tempting a double-billing retry.
        try
        {
            var total = await _billingClient.GetUsageTotalAsync(subscriptionId, component.Handle!, cancellationToken);
            if (total is not null)
            {
                result.PeriodToDateUnits = total.UnitBalance;
                var unitPrice = total.UnitPrice ?? component.UnitPrice;
                if (unitPrice.HasValue)
                {
                    result.PeriodToDateAmount = decimal.Round(total.UnitBalance * unitPrice.Value, 2, MidpointRounding.AwayFromZero);
                }
            }
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning($"Usage was recorded on subscription {subscriptionId} but the period-to-date total could not be read back: {ex.Message}");
        }

        return result;
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        var preview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
        preview.CurrentProductHandle ??= subscription.ProductHandle;
        return preview;
    }

    public async Task<PlanChangeResult> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, decimal? previewedPaymentDue, CancellationToken cancellationToken = default)
    {
        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        if (previewedPaymentDue.HasValue)
        {
            // Never apply a different amount than the one the customer confirmed.
            var fresh = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
            if (fresh.PaymentDue != previewedPaymentDue.Value)
            {
                throw new BillingValidationException(
                    $"The previewed amount {previewedPaymentDue.Value} is stale; the plan change now costs {fresh.PaymentDue}. Preview again before confirming.");
            }
        }

        var oldPlanHandle = subscription.ProductHandle ?? string.Empty;
        var changed = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(subscriptionId, oldPlanHandle, changed.ProductHandle ?? targetPlanHandle, changed.Balance),
            cancellationToken);

        var effectiveAt = timing == PlanChangeTiming.ImmediateWithProration
            ? changed.CurrentPeriodStartedAt ?? DateTimeOffset.UtcNow
            : changed.CurrentPeriodEndsAt;

        return new PlanChangeResult(
            Subscription.FromBilling(changed.CustomerReference ?? string.Empty, changed),
            oldPlanHandle,
            timing,
            effectiveAt);
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException($"Subscription {subscriptionId} was not found.");

        var oldState = subscription.State;
        GuardTransitionIsLegal(subscriptionId, action, oldState);

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel when cancellationTiming == CancellationTiming.EndOfPeriod
                => await _billingClient.CancelSubscriptionAtEndOfPeriodAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelSubscriptionAsync(subscriptionId, reason, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown lifecycle action.")
        };

        await PublishAsync(new SubscriptionStateChanged(subscriptionId, action, oldState, updated.State), cancellationToken);

        return Subscription.FromBilling(updated.CustomerReference ?? string.Empty, updated);
    }

    private async Task<BillingPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        return await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not exist in the billing provider. Re-seed the product family or correct the configured handle.");
    }

    /// <summary>
    /// Refuses to record usage unless the configured component handle really resolves to a
    /// metered-kind component on the family (UC2 precondition).
    /// </summary>
    private async Task<BillingComponent> EnsureMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException("No metered component handle is configured; usage cannot be recorded.");
        }

        var component = await _billingClient.GetComponentByHandleAsync(handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Metered component '{handle}' does not exist in the billing provider. Re-seed the product family or correct the configured handle.");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{component.Kind}', not metered; usage cannot be recorded against it.");
        }

        component.Handle ??= handle;
        return component;
    }

    private async Task<BillingSubscription> GetChangeablePlanSubscriptionAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException($"Subscription {subscriptionId} was not found.");

        if (string.Equals(subscription.ProductHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingValidationException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.", 400);
        }

        if (!IsLive(subscription.State))
        {
            throw new BillingValidationException(
                $"Subscription {subscriptionId} is '{subscription.State}' and cannot change plan; reactivate it first.");
        }

        // Fail against the configured seed rather than migrating onto a guessed plan.
        await ResolvePlanAsync(targetPlanHandle, cancellationToken);

        return subscription;
    }

    private static void GuardTransitionIsLegal(int subscriptionId, SubscriptionLifecycleAction action, string state)
    {
        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => IsLive(state),
            SubscriptionLifecycleAction.Resume => string.Equals(state, PausedState, StringComparison.OrdinalIgnoreCase),
            SubscriptionLifecycleAction.Cancel => IsLive(state) || string.Equals(state, PausedState, StringComparison.OrdinalIgnoreCase),
            SubscriptionLifecycleAction.Reactivate => ReactivatableStates.Contains(state, StringComparer.OrdinalIgnoreCase),
            _ => false
        };

        if (!legal)
        {
            throw new BillingValidationException(
                $"Subscription {subscriptionId} is '{state}'; '{action}' is not a legal transition from that state.", 409);
        }
    }

    private static bool IsLive(string state) => LiveStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Eventing is best-effort and in-process only (plan section 2.5): a handler that throws must
    /// never undo a change the provider has already applied.
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Publishing {notification.GetType().Name} failed after the provider call succeeded: {ex.Message}");
        }
    }
}
