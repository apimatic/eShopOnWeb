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
/// Orchestrates the subscription use cases: validate, call the billing client, publish the
/// in-process notification. Mirrors <see cref="OrderService"/>. The eShopOnWeb user is mapped to the
/// provider customer through the username/email reference, so no local persistence is needed.
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

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<Subscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Fail on a stale/unknown handle before creating anything, rather than enrolling in a guessed plan.
        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not resolve on the billing provider. Re-seed the sandbox (UC0) or correct the configured handles.");
        }

        // Idempotent on the user reference, so a repeated subscribe never duplicates the customer.
        var customer = await _billingClient.EnsureCustomerAsync(userName, cancellationToken);

        // A double-click must return the existing enrollment, never create a second one.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsLive);
        if (live is not null)
        {
            _logger.LogInformation($"User {userName} is already subscribed to {live.Plan.Handle} (subscription {live.Id}); returning the existing subscription.");
            return live;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(userName, subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        var customer = await _billingClient.EnsureCustomerAsync(userName, cancellationToken);
        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<Subscription?> GetLiveSubscriptionForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetSubscriptionsForUserAsync(userName, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsLive);
    }

    public async Task<UsageReport> RecordUsageAsync(string userName, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException($"Usage quantity must be greater than zero, but was {quantity}.");
        }

        var component = await GetValidatedMeteredComponentAsync(cancellationToken);

        var subscription = await GetLiveSubscriptionForUserAsync(userName, cancellationToken);
        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException($"User {userName} has no active subscription to record usage against.");
        }

        var record = await _billingClient.RecordUsageAsync(subscription.Id, component.Id, quantity, memo, cancellationToken);

        // The usage stands even if the read-back fails; report it with the total marked unavailable.
        decimal? periodToDateTotal = null;
        try
        {
            periodToDateTotal = await _billingClient.GetUsageTotalAsync(subscription.Id, component.Id, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning($"Recorded usage {record.Id} on subscription {subscription.Id} but could not read back the period-to-date total: {ex.Message}");
        }

        return new UsageReport(record, periodToDateTotal, component.UnitPrice);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userName, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetChangeablePlanSubscriptionAsync(userName, targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(string userName, string targetPlanHandle, PlanChangeTiming timing, int? previewedPaymentDueInCents, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetChangeablePlanSubscriptionAsync(userName, targetPlanHandle, cancellationToken);
        var previousPlan = subscription.Plan;

        // Never apply an amount other than the one the customer was shown.
        var current = await _billingClient.PreviewPlanChangeAsync(subscription, targetPlanHandle, timing, cancellationToken);
        if (previewedPaymentDueInCents.HasValue && previewedPaymentDueInCents.Value != current.PaymentDueInCents)
        {
            // Amounts are stated in minor units so the message never depends on the server's culture.
            throw new InvalidSubscriptionOperationException(
                $"The preview is stale: it quoted {previewedPaymentDueInCents.Value} cents but the change now costs {current.PaymentDueInCents} cents. Request a fresh preview before confirming.");
        }

        var changed = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(userName, changed, previousPlan, timing, current.PaymentDueInCents), cancellationToken);

        return changed;
    }

    public Task<Subscription> PauseAsync(string userName, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(userName,
            legalFrom: state => state is SubscriptionState.Active or SubscriptionState.PastDue,
            legalStates: "active",
            action: "paused",
            transition: (id, token) => _billingClient.PauseSubscriptionAsync(id, token),
            cancellationToken);
    }

    public Task<Subscription> ResumeAsync(string userName, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(userName,
            legalFrom: state => state is SubscriptionState.Paused,
            legalStates: "paused",
            action: "resumed",
            transition: (id, token) => _billingClient.ResumeSubscriptionAsync(id, token),
            cancellationToken);
    }

    public Task<Subscription> CancelAsync(string userName, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(userName,
            legalFrom: state => state is SubscriptionState.Active or SubscriptionState.PastDue or SubscriptionState.Paused,
            legalStates: "active or paused",
            action: "cancelled",
            transition: (id, token) => _billingClient.CancelSubscriptionAsync(id, timing, reason, token),
            cancellationToken);
    }

    public Task<Subscription> ReactivateAsync(string userName, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(userName,
            legalFrom: state => state is SubscriptionState.Cancelled,
            legalStates: "cancelled",
            action: "reactivated",
            transition: (id, token) => _billingClient.ReactivateSubscriptionAsync(id, token),
            cancellationToken);
    }

    /// <summary>
    /// Applies a lifecycle transition to the user's most relevant subscription, rejecting illegal
    /// transitions before any provider call and publishing old state to new state afterwards.
    /// </summary>
    private async Task<Subscription> TransitionAsync(string userName,
        System.Func<SubscriptionState, bool> legalFrom,
        string legalStates,
        string action,
        System.Func<int, CancellationToken, Task<Subscription>> transition,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        var subscriptions = await GetSubscriptionsForUserAsync(userName, cancellationToken);
        var subscription = subscriptions.FirstOrDefault(s => legalFrom(s.State)) ?? subscriptions.FirstOrDefault();

        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException($"User {userName} has no subscription to be {action}.");
        }

        if (!legalFrom(subscription.State))
        {
            throw new InvalidSubscriptionOperationException(
                $"A subscription in state '{subscription.ProviderState}' cannot be {action}; only a subscription that is {legalStates} can be.");
        }

        var previousState = subscription.State;
        var updated = await transition(subscription.Id, cancellationToken);

        await PublishAsync(new SubscriptionStateChanged(userName, updated, previousState), cancellationToken);

        return updated;
    }

    /// <summary>
    /// UC2's precondition: the configured component handle must resolve to a component of metered
    /// kind, otherwise usage is refused before anything is sent to the provider.
    /// </summary>
    private async Task<MeteredComponent> GetValidatedMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);

        if (component is null)
        {
            throw new BillingConfigurationException(
                "The configured metered component does not resolve on the billing provider. Re-seed the sandbox (UC0) before recording usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{component.Handle}' is of kind '{component.Kind}', not '{MeteredComponent.MeteredKind}'. A component cannot be type-converted in place — archive it and recreate it as metered (UC0).");
        }

        return component;
    }

    /// <summary>Resolves the subscription a plan change applies to, rejecting no-ops and illegal states.</summary>
    private async Task<Subscription> GetChangeablePlanSubscriptionAsync(string userName, string targetPlanHandle, CancellationToken cancellationToken)
    {
        var subscription = await GetLiveSubscriptionForUserAsync(userName, cancellationToken);
        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException(
                $"User {userName} has no active subscription to change. Reactivate an existing subscription first.");
        }

        if (string.Equals(subscription.Plan.Handle, targetPlanHandle, System.StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException($"The subscription is already on plan '{targetPlanHandle}'.");
        }

        return subscription;
    }

    /// <summary>
    /// Best-effort in-process publication (§2.5): a handler failure is logged and swallowed so the
    /// provider-side change, which has already succeeded, is never rolled back.
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning($"Publishing {notification.GetType().Name} failed after the provider call succeeded: {ex.Message}");
        }
    }
}
