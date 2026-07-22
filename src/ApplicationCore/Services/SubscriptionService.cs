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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases (UC1–UC4). Mirrors <see cref="OrderService"/>:
/// validate, call the billing client, announce the result through the in-process mediator.
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

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(
        CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<Subscription> SubscribeAsync(string buyerId, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Fail against a configuration error rather than enrolling against a guessed plan (UC1).
        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not resolve with the billing provider. Re-seed the sandbox or correct the configured handle.");
        }

        // Idempotent on the user reference, so a repeated subscribe reuses the customer record.
        var customer = await _billingClient.EnsureCustomerAsync(buyerId, buyerId, null, null, cancellationToken);

        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(
            customer.ProviderCustomerId, cancellationToken);

        // A double-click must never produce a second enrolment (UC1).
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive);
        if (alreadyActive is not null)
        {
            _logger.LogInformation(
                "Subscribe requested for {0} but subscription {1} is already active on plan {2}; returning it.",
                buyerId, alreadyActive.Id, alreadyActive.Plan.Handle);

            return alreadyActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(
            customer.ProviderCustomerId, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var customer = await _billingClient.EnsureCustomerAsync(buyerId, buyerId, null, null, cancellationToken);
        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(
            customer.ProviderCustomerId, cancellationToken);

        return new SubscriptionsByUserSpecification(buyerId).Evaluate(subscriptions).ToList();
    }

    public async Task<Subscription?> GetActiveSubscriptionForUserAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetSubscriptionsForUserAsync(buyerId, cancellationToken);

        return subscriptions.FirstOrDefault(s => s.IsActive);
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerBuyerId,
        decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        // Reject invalid input before any provider call (UC2).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException("record usage against",
                subscription.State, LegalActionsFor(subscription.State));
        }

        var component = await EnsureMeteredComponentAsync(cancellationToken);

        var recorded = await _billingClient.RecordUsageAsync(subscription.Id, component.Handle,
            quantity, memo, cancellationToken);

        // A failed read-back leaves the usage standing; report success with the total unavailable (UC2).
        decimal? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetPeriodToDateUsageAsync(subscription.Id,
                component.Handle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Recorded {0} unit(s) against subscription {1} but the period-to-date read-back failed: {2}",
                quantity, subscription.Id, ex.Message);

            periodToDate = null;
        }

        return new UsageReport(recorded, periodToDate, component.UnitPrice);
    }

    public async Task<UsageReport?> RecordUsageForUserAsync(string buyerId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var subscription = await GetActiveSubscriptionForUserAsync(buyerId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        return await RecordUsageAsync(subscription.Id, buyerId, quantity, memo, cancellationToken);
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);

        return await _billingClient.GetPeriodToDateUsageAsync(subscription.Id,
            _billingClient.MeteredComponentHandle, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string? ownerBuyerId,
        string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);

        await ValidatePlanChangeAsync(subscription, targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string? ownerBuyerId,
        string targetPlanHandle, PlanChangeTiming timing, PlanChangePreview? confirmedPreview,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);

        await ValidatePlanChangeAsync(subscription, targetPlanHandle, cancellationToken);

        // Never silently apply an amount other than the one the customer was shown (UC3).
        if (confirmedPreview is not null)
        {
            var fresh = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle,
                timing, cancellationToken);

            if (!fresh.Matches(confirmedPreview))
            {
                throw new StalePlanChangePreviewException();
            }
        }

        var previousPlanHandle = subscription.Plan.Handle;

        var changed = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(changed, previousPlanHandle, timing, confirmedPreview),
            cancellationToken);

        return changed;
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, string? ownerBuyerId,
        DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);
        EnsureTransitionIsLegal("pause", subscription.State, CanPause);

        var paused = await _billingClient.PauseAsync(subscription.Id, automaticallyResumeAt, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(paused, subscription.State, "pause"), cancellationToken);

        return paused;
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);
        EnsureTransitionIsLegal("resume", subscription.State, CanResume);

        var resumed = await _billingClient.ResumeAsync(subscription.Id, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(resumed, subscription.State, "resume"), cancellationToken);

        return resumed;
    }

    public async Task<Subscription> CancelAsync(int subscriptionId, string? ownerBuyerId,
        CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);
        EnsureTransitionIsLegal("cancel", subscription.State, CanCancel);

        var canceled = await _billingClient.CancelAsync(subscription.Id, timing, reason, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(canceled, subscription.State, "cancel"), cancellationToken);

        return canceled;
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveSubscriptionAsync(subscriptionId, ownerBuyerId, cancellationToken);
        EnsureTransitionIsLegal("reactivate", subscription.State, CanReactivate);

        var reactivated = await _billingClient.ReactivateAsync(subscription.Id, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(reactivated, subscription.State, "reactivate"), cancellationToken);

        return reactivated;
    }

    private async Task<Subscription> ResolveSubscriptionAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (subscription is null)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        // A null ownerBuyerId means an administrator acting on any subscription.
        if (ownerBuyerId is not null
            && !string.Equals(subscription.BuyerId, ownerBuyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    /// <summary>
    /// UC2 precondition — refuses to record usage unless the configured component handle
    /// resolves to a component of metered kind.
    /// </summary>
    private async Task<MeteredComponent> EnsureMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var handle = _billingClient.MeteredComponentHandle;

        var component = await _billingClient.GetComponentByHandleAsync(handle, cancellationToken);

        if (component is null)
        {
            throw new BillingConfigurationException(
                $"Metered component '{handle}' does not resolve on the configured product family. Re-seed the sandbox before recording usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is not of metered kind and cannot accrue pay-as-you-go usage. Archive it and recreate it as metered.");
        }

        return component;
    }

    private async Task ValidatePlanChangeAsync(Subscription subscription, string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        // Reject a no-op before any provider call (UC3).
        if (string.Equals(subscription.Plan.Handle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.", nameof(targetPlanHandle));
        }

        EnsureTransitionIsLegal("change the plan of", subscription.State, CanChangePlan);

        var target = await _billingClient.GetPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (target is null)
        {
            throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve with the billing provider. Re-seed the sandbox or correct the configured handle.");
        }
    }

    private static void EnsureTransitionIsLegal(string action, SubscriptionState state,
        Func<SubscriptionState, bool> isLegal)
    {
        if (!isLegal(state))
        {
            throw new InvalidSubscriptionTransitionException(action, state, LegalActionsFor(state));
        }
    }

    private static bool CanPause(SubscriptionState state) =>
        state == SubscriptionState.Active || state == SubscriptionState.Trialing;

    private static bool CanResume(SubscriptionState state) =>
        state == SubscriptionState.Paused;

    private static bool CanCancel(SubscriptionState state) =>
        state != SubscriptionState.Canceled
        && state != SubscriptionState.Expired
        && state != SubscriptionState.FailedToCreate;

    private static bool CanReactivate(SubscriptionState state) =>
        state == SubscriptionState.Canceled
        || state == SubscriptionState.Expired
        || state == SubscriptionState.TrialEnded
        || state == SubscriptionState.Unpaid;

    private static bool CanChangePlan(SubscriptionState state) =>
        state == SubscriptionState.Active || state == SubscriptionState.Trialing;

    private static IReadOnlyCollection<string> LegalActionsFor(SubscriptionState state)
    {
        var actions = new List<string>();

        if (CanPause(state)) actions.Add("pause");
        if (CanResume(state)) actions.Add("resume");
        if (CanCancel(state)) actions.Add("cancel");
        if (CanReactivate(state)) actions.Add("reactivate");
        if (CanChangePlan(state)) actions.Add("change plan");

        return actions.Count > 0 ? actions : new List<string> { "none" };
    }

    /// <summary>
    /// Publishes in-process, best-effort. A handler failure never rolls back the provider-side
    /// change that has already succeeded (§2.5).
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process handler for {0} failed after the billing change succeeded: {1}",
                notification.GetType().Name, ex.Message);
        }
    }
}
