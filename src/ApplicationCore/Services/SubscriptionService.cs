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
/// Orchestrates the subscription use cases (plan.md §3), mirroring <see cref="OrderService"/>:
/// validate, call the billing client, publish the MediatR notification. The provider is reached
/// only through <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    // UC2 precondition: the configured component must resolve to a metered component. Verified on
    // first use and remembered, so the check costs one call rather than one per usage report.
    private MeteredComponent? _verifiedMeteredComponent;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Resolve the plan from its durable handle rather than trusting a configured numeric id,
        // which goes stale whenever the catalogue is re-seeded (UC0 / UC1 failure scenario).
        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken)
            ?? throw new PlanNotFoundException(planHandle);

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' is archived and cannot be subscribed to. Check the product family seed (UC0).");
        }

        // Idempotent on the user reference, so a failed enrolment can be retried safely (UC1).
        var customer = await _billingClient.EnsureCustomerAsync(
            userReference, userReference, DeriveFirstName(userReference), DeriveLastName(), cancellationToken);

        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsLive);
        if (live is not null)
        {
            // A repeated subscribe (double-click) returns what is already there rather than
            // enrolling twice; a different plan is a plan change, not a second enrolment (UC3).
            if (live.PlanHandle == planHandle)
            {
                _logger.LogInformation(
                    "Subscribe for {0} is a no-op: subscription {1} is already live on {2}.",
                    userReference, live.Id, planHandle);
                return live;
            }

            throw new ActiveSubscriptionExistsException(live.Id, live.PlanHandle, planHandle);
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

    public async Task<UsageSummary> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        // Rejected before anything reaches the provider (UC2 failure scenario).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetLiveSubscriptionAsync(subscriptionId, SubscriptionActions.RecordUsage, cancellationToken);
        var component = await EnsureMeteredComponentAsync(cancellationToken);

        await _billingClient.RecordUsageAsync(subscriptionId, component.Handle, quantity, memo, cancellationToken);

        try
        {
            return await BuildUsageSummaryAsync(subscription, component, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            // The usage itself was recorded — reporting the whole operation as failed would invite
            // a retry and double-bill the units (UC2 failure scenario).
            _logger.LogWarning(
                "Usage was recorded against subscription {0} but the running total could not be read back: {1}",
                subscriptionId, ex.Message);
            return UsageSummary.Unavailable(subscriptionId, component.Handle);
        }
    }

    public async Task<UsageSummary> GetUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);
        var component = await EnsureMeteredComponentAsync(cancellationToken);

        return await BuildUsageSummaryAsync(subscription, component, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var (subscription, targetPlan) = await ValidatePlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // Deferred changes are not prorated: the customer simply pays the new plan price from
            // the next period (UC3 step 2).
            return new PlanChangePreview(subscriptionId, subscription.PlanHandle, targetPlanHandle, timing,
                proratedAdjustmentInCents: 0,
                chargeInCents: targetPlan.PriceInCents,
                paymentDueInCents: 0,
                creditAppliedInCents: 0);
        }

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        PlanChangePreview? confirmedPreview,
        CancellationToken cancellationToken = default)
    {
        var (subscription, _) = await ValidatePlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);

        if (confirmedPreview is not null)
        {
            // Never apply an amount other than the one the customer was shown (UC3 failure scenario).
            var current = await PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
            if (!current.QuotesSameAmountsAs(confirmedPreview))
            {
                throw new StalePlanChangePreviewException(subscriptionId);
            }
        }

        var previousPlanHandle = subscription.PlanHandle;
        var changed = await InvokeProviderAsync(subscriptionId, SubscriptionActions.ChangePlan,
            () => _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken),
            cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(changed, previousPlanHandle, timing, confirmedPreview), cancellationToken);

        return changed;
    }

    public Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(subscriptionId, SubscriptionActions.Pause,
            s => s.CanPause,
            () => _billingClient.PauseAsync(subscriptionId, null, cancellationToken),
            cancellationToken);

    public Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(subscriptionId, SubscriptionActions.Resume,
            s => s.CanResume,
            () => _billingClient.ResumeAsync(subscriptionId, cancellationToken),
            cancellationToken);

    public Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(subscriptionId, SubscriptionActions.Cancel,
            s => s.CanCancel,
            () => _billingClient.CancelAsync(subscriptionId, timing, reason, cancellationToken),
            cancellationToken);

    public Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(subscriptionId, SubscriptionActions.Reactivate,
            s => s.CanReactivate,
            () => _billingClient.ReactivateAsync(subscriptionId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// The shared UC4 shape: read current state, refuse an illegal transition without calling the
    /// provider, apply it, then announce old → new.
    /// </summary>
    private async Task<Subscription> TransitionAsync(int subscriptionId,
        string action,
        Func<Subscription, bool> isLegal,
        Func<Task<Subscription>> transition,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        subscription.EnsureCanTransition(action, isLegal(subscription));

        var previousState = subscription.State;
        var updated = await InvokeProviderAsync(subscriptionId, action, transition, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionStateChanged(updated, previousState, action), cancellationToken);

        return updated;
    }

    /// <summary>
    /// Runs a provider mutation. If the provider refuses a transition the local check allowed, the
    /// state has drifted out-of-band (there are no webhooks, plan.md §7) — so the provider's state
    /// is re-read and reported as truth rather than the stale local view (UC4 failure scenario).
    /// </summary>
    private async Task<Subscription> InvokeProviderAsync(int subscriptionId,
        string action,
        Func<Task<Subscription>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (BillingProviderException ex)
        {
            var current = await TryReadStateAsync(subscriptionId, cancellationToken);
            if (current is null)
            {
                throw;
            }

            throw new BillingProviderException(
                $"The billing provider refused to '{action}' subscription {subscriptionId}, which it reports as " +
                $"'{current.State}'. {ex.Message}", ex);
        }
    }

    private async Task<Subscription?> TryReadStateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException refreshFailure)
        {
            _logger.LogWarning("Could not refresh subscription {0} after a failed operation: {1}",
                subscriptionId, refreshFailure.Message);
            return null;
        }
    }

    private async Task<(Subscription Subscription, SubscriptionPlan TargetPlan)> ValidatePlanChangeAsync(
        int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.Ordinal))
        {
            throw new PlanChangeNotApplicableException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.");
        }

        subscription.EnsureCanTransition(SubscriptionActions.ChangePlan, subscription.CanChangePlan);

        var targetPlan = await _billingClient.GetPlanByHandleAsync(targetPlanHandle, cancellationToken)
            ?? throw new PlanNotFoundException(targetPlanHandle);

        if (targetPlan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Plan '{targetPlanHandle}' is archived and cannot be changed to. Check the product family seed (UC0).");
        }

        return (subscription, targetPlan);
    }

    private async Task<Subscription> GetLiveSubscriptionAsync(int subscriptionId, string action, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        subscription.EnsureCanTransition(action, subscription.IsLive);

        return subscription;
    }

    /// <summary>
    /// UC2 precondition — refuse to record usage unless the configured component really is metered.
    /// </summary>
    private async Task<MeteredComponent> EnsureMeteredComponentAsync(CancellationToken cancellationToken)
    {
        if (_verifiedMeteredComponent is not null)
        {
            return _verifiedMeteredComponent;
        }

        var handle = _billingClient.MeteredComponentHandle;
        var component = await _billingClient.GetComponentByHandleAsync(handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"The configured metered component '{handle}' does not exist on the product family. Fix the seed (UC0) before recording usage.");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"The configured component '{handle}' is of kind '{component.Kind}', not metered. " +
                "A component's kind cannot be converted in place — archive it and recreate it as metered (UC0).");
        }

        _verifiedMeteredComponent = component;
        return component;
    }

    private async Task<UsageSummary> BuildUsageSummaryAsync(Subscription subscription,
        MeteredComponent component,
        CancellationToken cancellationToken)
    {
        var periodStart = subscription.CurrentPeriodStartedAt;

        var records = await _billingClient.ListUsageAsync(subscription.Id, component.Handle, periodStart, cancellationToken);

        // The provider filters from midnight on the given date, so trim to the exact period start
        // to keep the running total scoped to the period that will actually be invoiced.
        var periodRecords = periodStart is null
            ? records
            : records.Where(r => r.CreatedAt >= periodStart.Value).ToList();

        return UsageSummary.Available(subscription.Id,
            component.Handle,
            periodRecords.Sum(r => r.Quantity),
            component.UnitPrice,
            periodStart,
            subscription.CurrentPeriodEndsAt,
            periodRecords.ToList());
    }

    /// <summary>
    /// Eventing is in-process and best-effort: a handler that throws is logged, never allowed to
    /// undo work the provider has already committed (plan.md §2.5).
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Publishing {0} failed after the provider call succeeded: {1}",
                notification.GetType().Name, ex.Message);
        }
    }

    private static string DeriveFirstName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? userReference : localPart;
    }

    private static string DeriveLastName() => "eShopOnWeb";
}
