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
/// Orchestrates the subscription use cases (mirrors <see cref="OrderService"/>): validates,
/// calls the provider-agnostic billing client, and publishes the corresponding MediatR
/// notification. Holds no persisted state — the userId &lt;-&gt; subscription mapping is
/// resolved on demand from the customer reference (§8: stateless mapping).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const decimal ProrationAmountTolerance = 0.01m;

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    private readonly SemaphoreSlim _componentValidationGate = new(1, 1);
    private bool _meteredComponentValidated;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string customerReference, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        await EnsurePlanHandleResolvesAsync(planHandle, cancellationToken);

        await _billingClient.EnsureCustomerAsync(customerReference, email: customerReference, firstName, lastName, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customerReference, cancellationToken);
        var existingActive = existingSubscriptions.FirstOrDefault(s => !s.Status.IsTerminal());
        if (existingActive is not null)
        {
            _logger.LogInformation("Customer {0} already has subscription {1}; skipping duplicate enrollment", customerReference, existingActive.Id);
            return existingActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customerReference, planHandle, cancellationToken);

        try
        {
            await _publisher.Publish(new SubscriptionActivated(customerReference, subscription.Id, planHandle), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish SubscriptionActivated for subscription {0}: {1}", subscription.Id, ex.Message);
        }

        return subscription;
    }

    public Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        return _billingClient.ListCustomerSubscriptionsAsync(customerReference, cancellationToken);
    }

    public async Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        await EnsureMeteredComponentValidatedAsync(cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!subscription.Status.IsActiveLike())
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status.ToString(), "record usage");
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await ValidatePlanChangeIsLegalAsync(subscriptionId, targetPlanHandle, cancellationToken);
        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, applyNow, cancellationToken);
    }

    public async Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, decimal expectedProratedAmount, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscriptionBefore = await ValidatePlanChangeIsLegalAsync(subscriptionId, targetPlanHandle, cancellationToken);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, applyNow, cancellationToken);
        if (Math.Abs(freshPreview.ProratedAmount - expectedProratedAmount) > ProrationAmountTolerance)
        {
            throw new PlanChangePreviewStaleException(subscriptionId);
        }

        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetPlanHandle, applyNow, cancellationToken);

        try
        {
            await _publisher.Publish(
                new SubscriptionPlanChanged(subscriptionBefore.CustomerReference, subscriptionId, subscriptionBefore.PlanHandle, targetPlanHandle, freshPreview.EffectiveDate),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish SubscriptionPlanChanged for subscription {0}: {1}", subscriptionId, ex.Message);
        }

        return updated;
    }

    public Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "pause",
            current => !current.IsPaused() && !current.IsTerminal(),
            () => _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            cancellationToken);

    public Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "resume",
            current => current.IsPaused(),
            () => _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            cancellationToken);

    public Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            endOfPeriod ? "cancel at end of period" : "cancel",
            current => !current.IsTerminal(),
            () => _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken),
            cancellationToken);

    public Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            subscriptionId,
            "reactivate",
            current => current.CanReactivate(),
            () => _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Shared shape for every UC4 lifecycle action: re-read the current state, reject illegal
    /// transitions before any provider call, invoke the provider, publish the state-change
    /// notification, and — if the provider itself rejects a transition the local check allowed
    /// (state drifted out-of-band) — refresh the local view and surface the conflict.
    /// </summary>
    private async Task<Subscription> TransitionAsync(
        int subscriptionId,
        string transitionName,
        Func<SubscriptionStatus, bool> isLegalFromState,
        Func<Task<Subscription>> performTransition,
        CancellationToken cancellationToken)
    {
        var before = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!isLegalFromState(before.Status))
        {
            throw new InvalidSubscriptionStateException(subscriptionId, before.Status.ToString(), transitionName);
        }

        Subscription after;
        try
        {
            after = await performTransition();
        }
        catch (BillingProviderException ex)
        {
            var refreshed = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
            throw new InvalidSubscriptionStateException(subscriptionId, refreshed.Status.ToString(), transitionName, ex);
        }

        try
        {
            await _publisher.Publish(new SubscriptionStateChanged(before.CustomerReference, subscriptionId, before.Status, after.Status), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish SubscriptionStateChanged for subscription {0}: {1}", subscriptionId, ex.Message);
        }

        return after;
    }

    private async Task<Subscription> ValidatePlanChangeIsLegalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken)
    {
        await EnsurePlanHandleResolvesAsync(targetPlanHandle, cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status.ToString(), $"change to plan '{targetPlanHandle}' (already on that plan)");
        }

        if (subscription.Status.IsTerminal())
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.Status.ToString(), "change plan");
        }

        return subscription;
    }

    private async Task EnsurePlanHandleResolvesAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingConfigurationException(
                $"Plan handle '{planHandle}' does not resolve against the billing provider. Re-run UC0 seeding or correct the configured handle.");
        }
    }

    private async Task EnsureMeteredComponentValidatedAsync(CancellationToken cancellationToken)
    {
        if (_meteredComponentValidated)
        {
            return;
        }

        await _componentValidationGate.WaitAsync(cancellationToken);
        try
        {
            if (_meteredComponentValidated)
            {
                return;
            }

            var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
            if (!component.IsMetered)
            {
                throw new BillingConfigurationException(
                    $"Configured usage component '{component.Handle}' does not resolve to a metered-kind component. Re-run UC0 seeding before recording usage.");
            }

            _meteredComponentValidated = true;
        }
        finally
        {
            _componentValidationGate.Release();
        }
    }
}
