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
/// Orchestrates the subscription use cases (mirrors <see cref="OrderService"/>): validates the
/// request against eShopOnWeb's own rules, drives the billing client, then announces the result
/// through the in-process mediator. Per §8 the userId ↔ subscription mapping is stateless — the
/// provider-side customer reference is the link, which is what makes subscribe idempotent.
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

    public async Task<Subscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // A handle that no longer resolves means the sandbox was reseeded — a configuration error
        // pointing back at UC0. Never enroll against a guessed plan.
        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingProviderException(
                $"Plan '{planHandle}' does not resolve in the configured product family. Check the Maxio configuration against the seeded plans (UC0).");
        }

        // Double-click / repeated call: return the enrollment that already exists rather than
        // creating a second one.
        var existing = await _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive);
        if (alreadyActive is not null)
        {
            _logger.LogInformation(
                "Subscribe for {0} skipped: subscription {1} is already active on plan {2}.",
                userReference, alreadyActive.Id, alreadyActive.PlanHandle);
            return alreadyActive;
        }

        var (firstName, lastName) = SplitName(userReference);
        await _billingClient.EnsureCustomerAsync(userReference, userReference, firstName, lastName, cancellationToken);

        var subscription = await _billingClient.CreateSubscriptionAsync(userReference, plan.Handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        return _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        // Reject invalid input before any provider call (UC2 failure scenarios).
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero, but was {quantity}.");
        }

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} was not found.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage cannot be reported for subscription {subscriptionId} because it is {subscription.State}, not active.");
        }

        var componentHandle = _billingClient.MeteredComponentHandle;
        var record = await _billingClient.RecordUsageAsync(subscriptionId, componentHandle, quantity, memo, cancellationToken);

        // The usage stands even if the read-back fails; report success with the total unavailable
        // rather than failing the whole operation.
        UsageSummary? summary = null;
        try
        {
            summary = await _billingClient.GetUsageSummaryAsync(subscriptionId, componentHandle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Recorded usage {0} on subscription {1} but the period-to-date total could not be read back: {2}",
                quantity, subscriptionId, ex.Message);
        }

        return new UsageReport(record, summary);
    }

    public async Task<UsageReport?> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
        var active = subscriptions.FirstOrDefault(s => s.IsActive);
        if (active is null)
        {
            _logger.LogInformation("No active subscription for {0}; no usage recorded.", userReference);
            return null;
        }

        return await RecordUsageAsync(active.Id, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? expectedNetAmount = null, CancellationToken cancellationToken = default)
    {
        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        // Never silently apply a different amount than the one the customer was shown: re-price the
        // change and reject the commit if the basis moved since the preview.
        if (expectedNetAmount.HasValue)
        {
            var fresh = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);
            if (fresh.NetAmount != expectedNetAmount.Value)
            {
                throw new InvalidSubscriptionOperationException(
                    $"The proration preview is stale: it showed {expectedNetAmount.Value:N2} but the change now costs {fresh.NetAmount:N2}. Review a fresh preview before confirming.");
            }
        }

        var previousPlanHandle = subscription.PlanHandle;
        var changed = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(changed, previousPlanHandle, timing), cancellationToken);

        return changed;
    }

    public Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(subscriptionId,
            current => current.IsActive,
            "pause",
            "only an active subscription can be paused",
            (id, token) => _billingClient.PauseAsync(id, token),
            cancellationToken);
    }

    public Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(subscriptionId,
            current => current.IsPaused,
            "resume",
            "only a paused subscription can be resumed",
            (id, token) => _billingClient.ResumeAsync(id, token),
            cancellationToken);
    }

    public Task<Subscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default)
    {
        return TransitionAsync(subscriptionId,
            current => !current.IsCanceled,
            "cancel",
            "the subscription is already cancelled",
            (id, token) => _billingClient.CancelAsync(id, timing, reason, token),
            cancellationToken);
    }

    public Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        return TransitionAsync(subscriptionId,
            current => !current.IsActive,
            "reactivate",
            "the subscription is already active",
            (id, token) => _billingClient.ReactivateAsync(id, token),
            cancellationToken);
    }

    /// <summary>
    /// Reads the subscription, refuses the transition when it is illegal from the current state
    /// (making no provider call), applies it, then announces old → new state.
    /// </summary>
    private async Task<Subscription> TransitionAsync(int subscriptionId,
        Func<Subscription, bool> isLegal,
        string action,
        string reasonWhenIllegal,
        Func<int, CancellationToken, Task<Subscription>> transition,
        CancellationToken cancellationToken)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var current = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (current is null)
        {
            throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} was not found.");
        }

        if (!isLegal(current))
        {
            throw new InvalidSubscriptionOperationException(
                $"Cannot {action} subscription {subscriptionId}: it is {current.State} and {reasonWhenIllegal}.");
        }

        var previousState = current.State;
        var updated = await transition(subscriptionId, cancellationToken);

        await PublishAsync(new SubscriptionStateChanged(updated, previousState), cancellationToken);

        return updated;
    }

    private async Task<Subscription> GetChangeablePlanSubscriptionAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} was not found.");
        }

        // A cancelled subscription must be reactivated (UC4) before it can move plan.
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is {subscription.State} and cannot change plan. Reactivate it first.");
        }

        // A change to the plan already in use is a no-op — reject it before any provider call.
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.");
        }

        var target = await _billingClient.GetPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (target is null)
        {
            throw new BillingProviderException(
                $"Target plan '{targetPlanHandle}' does not resolve in the configured product family. Check the Maxio configuration against the seeded plans (UC0).");
        }

        return subscription;
    }

    /// <summary>
    /// Publishes best-effort: there is no outbox, so a handler failure is logged and swallowed —
    /// the provider-side change already stands and must not be rolled back (§2.5).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process notification {0} failed after a successful provider call: {1}",
                notification.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Derives a display name from the eShopOnWeb user reference (an email/username, §4.4), because
    /// the provider requires first and last name when creating a customer.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : userReference;
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
