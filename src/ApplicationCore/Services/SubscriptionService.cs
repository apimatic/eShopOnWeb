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

/// <summary>
/// Orchestrates the subscription use cases over the provider-agnostic billing seam,
/// mirroring <see cref="OrderService"/>. Validates first, calls the billing client, then
/// announces the change with a best-effort in-process MediatR notification (plan §2.5).
/// </summary>
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

    public Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string? planHandle = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var handle = string.IsNullOrWhiteSpace(planHandle) ? _billingClient.Catalog.DefaultPlanHandle : planHandle;
        var plan = await ResolvePlanAsync(handle, cancellationToken);

        var customer = await _billingClient.EnsureCustomerAsync(userReference, userReference, cancellationToken);

        // A repeated subscribe (double-click, retried call) must never create a second enrollment.
        var existing = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var active = existing.FirstOrDefault(s => SubscriptionStates.IsLive(s.State));
        if (active is not null)
        {
            _logger.LogInformation($"Customer {customer.Id} already holds active subscription {active.Id}; returning it instead of enrolling again.");
            return ToSubscription(userReference, active);
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, handle, cancellationToken);
        var subscription = ToSubscription(userReference, created);

        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
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

        return subscriptions.Select(s => ToSubscription(userReference, s)).ToArray();
    }

    public async Task<UsageReceipt> RecordUsageAsync(string? userReference, int subscriptionId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        // Reject invalid input before anything is sent to the provider (UC2).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var componentHandle = _billingClient.Catalog.MeteredComponentHandle;
        var component = await _billingClient.GetComponentByHandleAsync(componentHandle, cancellationToken);
        if (component is null)
        {
            throw new BillingConfigurationException($"The configured metered component handle '{componentHandle}' does not resolve. Seed the billing provider as described in UC0 before recording usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException($"The configured component '{componentHandle}' is of kind '{component.Kind}', not '{BillingComponent.MeteredKind}'. A component cannot be converted in place — archive it and recreate it as metered (UC0).");
        }

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, userReference, cancellationToken);
        if (!SubscriptionStates.IsLive(subscription.State))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscriptionId} is '{subscription.State}' and cannot accrue usage; only an active subscription can.",
                subscription.State);
        }

        var receipt = await _billingClient.RecordUsageAsync(subscriptionId, componentHandle, quantity, memo, cancellationToken);

        // The usage stands even if the read-back fails; report success with the total unavailable.
        decimal? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetUsageTotalAsync(subscriptionId, componentHandle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning($"Usage was recorded against subscription {subscriptionId} but the period-to-date total could not be read back: {ex.Message}");
            periodToDate = null;
        }

        return new UsageReceipt
        {
            Id = receipt.Id,
            SubscriptionId = subscriptionId,
            ComponentId = receipt.ComponentId == 0 ? component.Id : receipt.ComponentId,
            ComponentHandle = receipt.ComponentHandle ?? componentHandle,
            Quantity = receipt.Quantity,
            Memo = receipt.Memo ?? memo,
            RecordedAt = receipt.RecordedAt,
            PeriodToDateTotal = periodToDate
        };
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string? userReference, int subscriptionId, string targetPlanHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, userReference, cancellationToken);

        return await BuildPreviewAsync(subscription, targetPlanHandle, applyImmediately, cancellationToken);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(string? userReference, int subscriptionId, string targetPlanHandle, bool applyImmediately, decimal? confirmedPaymentDue = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, userReference, cancellationToken);
        var previousPlanHandle = subscription.ProductHandle ?? string.Empty;

        // Re-price at commit time; never apply an amount other than the one the customer saw.
        var preview = await BuildPreviewAsync(subscription, targetPlanHandle, applyImmediately, cancellationToken);
        if (confirmedPaymentDue.HasValue && confirmedPaymentDue.Value != preview.PaymentDue)
        {
            throw new StalePlanChangePreviewException(confirmedPaymentDue.Value, preview.PaymentDue);
        }

        var changed = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, applyImmediately, cancellationToken);
        var updated = ToSubscription(changed.CustomerReference ?? subscription.CustomerReference, changed);
        var effectiveAt = applyImmediately ? DateTimeOffset.UtcNow : changed.CurrentPeriodEndsAt;

        await PublishAsync(new SubscriptionPlanChanged(subscriptionId, previousPlanHandle, targetPlanHandle, preview.PaymentDue, effectiveAt), cancellationToken);

        return new PlanChangeResult(updated, previousPlanHandle, preview, effectiveAt);
    }

    public async Task<SubscriptionLifecycleResult> ApplyLifecycleActionAsync(string? userReference, int subscriptionId, SubscriptionLifecycleAction action, bool endOfPeriod = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, userReference, cancellationToken);
        var previousState = subscription.State;

        if (!SubscriptionStates.IsTransitionLegal(action, previousState))
        {
            var legal = SubscriptionStates.LegalTransitionsFrom(previousState);
            var legalText = legal.Count == 0 ? "none" : string.Join(", ", legal);
            throw new InvalidSubscriptionStateException(
                $"Cannot {action} a subscription in state '{previousState}'. Legal transitions from this state: {legalText}.",
                previousState);
        }

        var result = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
        };

        var effectiveAt = action == SubscriptionLifecycleAction.Cancel && endOfPeriod
            ? result.DelayedCancelAt ?? result.CurrentPeriodEndsAt
            : DateTimeOffset.UtcNow;

        await PublishAsync(new SubscriptionStateChanged(subscriptionId, action, previousState, result.State, effectiveAt), cancellationToken);

        var updated = ToSubscription(result.CustomerReference ?? subscription.CustomerReference, result);

        return new SubscriptionLifecycleResult(updated, action, previousState, effectiveAt);
    }

    private async Task<BillingPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingConfigurationException("No subscription plan handle was supplied and no default plan is configured. Configure 'Maxio:DefaultProductHandle' (UC0).");
        }

        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null || plan.Archived)
        {
            throw new BillingConfigurationException($"The plan handle '{planHandle}' does not resolve to an available plan. Seed the billing provider as described in UC0 and refresh the configured identifiers.");
        }

        return plan;
    }

    private async Task<PlanChangePreview> BuildPreviewAsync(BillingSubscription subscription, string targetPlanHandle, bool applyImmediately, CancellationToken cancellationToken)
    {
        // The handle the caller asked for is what is previewed, committed and reported back;
        // resolving it only proves the plan exists and supplies its price.
        var target = await ResolvePlanAsync(targetPlanHandle, cancellationToken);

        if (string.Equals(subscription.ProductHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'; a plan change would be a no-op.",
                subscription.State);
        }

        if (!SubscriptionStates.IsLive(subscription.State))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscription.Id} is '{subscription.State}' and cannot change plan. Reactivate it first.",
                subscription.State);
        }

        if (!applyImmediately)
        {
            // Deferred changes take effect at the period boundary, so nothing is prorated:
            // the customer simply pays the new plan price from the next period.
            return new PlanChangePreview
            {
                CurrentPlanHandle = subscription.ProductHandle ?? string.Empty,
                TargetPlanHandle = targetPlanHandle,
                ApplyImmediately = false,
                ProratedAdjustment = decimal.Zero,
                Charge = target.Price,
                PaymentDue = decimal.Zero,
                CreditApplied = decimal.Zero
            };
        }

        var preview = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, cancellationToken);

        return new PlanChangePreview
        {
            CurrentPlanHandle = subscription.ProductHandle ?? string.Empty,
            TargetPlanHandle = targetPlanHandle,
            ApplyImmediately = true,
            ProratedAdjustment = preview.ProratedAdjustment,
            Charge = preview.Charge,
            PaymentDue = preview.PaymentDue,
            CreditApplied = preview.CreditApplied
        };
    }

    private async Task<BillingSubscription> GetSubscriptionOrThrowAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        return subscription ?? throw new SubscriptionNotFoundException(subscriptionId);
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(int subscriptionId, string? userReference, CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionOrThrowAsync(subscriptionId, cancellationToken);

        // A customer may only act on their own subscription; admins pass no reference.
        if (userReference is not null && !string.Equals(subscription.CustomerReference, userReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private static Subscription ToSubscription(string? userReference, BillingSubscription subscription) =>
        new(string.IsNullOrWhiteSpace(userReference) ? subscription.CustomerId.ToString() : userReference,
            subscription.CustomerId,
            subscription.Id,
            subscription.ProductHandle ?? string.Empty,
            subscription.ProductName ?? string.Empty,
            subscription.ProductPrice,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod);

    /// <summary>
    /// Eventing is in-process and best-effort: a handler failure never rolls back a change the
    /// provider has already applied (plan §2.5).
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
