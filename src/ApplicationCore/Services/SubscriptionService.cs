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
/// Orchestrates the subscription use cases, mirroring <see cref="OrderService"/>: validate, drive
/// the billing provider through <see cref="IBillingClient"/>, then announce the change in-process.
/// <para>
/// Notifications are published only after the provider call has succeeded, and publication failure
/// never undoes the billing action (plan.md §2.5).
/// </para>
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

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<CustomerSubscription> SubscribeAsync(string userReference,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Fail on an unresolvable handle before creating anything, so a stale configuration can
        // never enrol a customer against a guessed plan (UC1 failure scenario).
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken)
            ?? throw new BillingConfigurationException($"Plan handle '{planHandle}' does not resolve to a plan.");

        var customer = await _billingClient.EnsureCustomerAsync(userReference, userReference, cancellationToken);

        // A repeated subscribe (double-click, retried call) must never create a second enrolment.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(subscription => subscription.IsLive);

        if (live is not null)
        {
            if (string.Equals(live.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Subscribe for {0} is a no-op; subscription {1} is already active on '{2}'.",
                    userReference, live.Id, plan.Handle);
                return live;
            }

            throw new DuplicateSubscriptionException(live.Id, live.PlanHandle ?? "(unknown)", plan.Handle);
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(created.Id,
            userReference,
            plan.Handle,
            created.PlanName ?? plan.Name,
            created.PlanPrice == decimal.Zero ? plan.Price : created.PlanPrice,
            created.NextBillingAt), cancellationToken);

        return created;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<CustomerSubscription> GetMySubscriptionAsync(string userReference,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        // A subscription owned by somebody else is reported exactly as a missing one, so ownership
        // cannot be probed by comparing responses.
        if (subscription is null || !OwnedBy(subscription, userReference))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    public async Task<UsageSummary> RecordUsageAsync(string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken)
            ?? throw new NoActiveSubscriptionException(userReference);

        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = subscriptions.FirstOrDefault(subscription => subscription.IsLive)
            ?? throw new NoActiveSubscriptionException(userReference);

        return await RecordUsageCoreAsync(live, quantity, memo, cancellationToken);
    }

    public async Task<UsageSummary> RecordUsageForSubscriptionAsync(int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        if (!subscription.IsLive)
        {
            throw new NoActiveSubscriptionException(subscriptionId, subscription.State);
        }

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetMySubscriptionAsync(userReference, subscriptionId, cancellationToken);
        EnsurePlanChangeAllowed(subscription, targetPlanHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewSignature,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));
        Guard.Against.NullOrEmpty(previewSignature, nameof(previewSignature));

        var subscription = await GetMySubscriptionAsync(userReference, subscriptionId, cancellationToken);
        EnsurePlanChangeAllowed(subscription, targetPlanHandle);

        // Re-price the change and refuse to commit if the basis moved since the customer confirmed,
        // so the amount charged is always the amount that was shown (UC3).
        var current = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
        if (!string.Equals(current.Signature, previewSignature, StringComparison.Ordinal))
        {
            throw new StalePlanChangePreviewException(subscriptionId);
        }

        var updated = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(subscriptionId,
            userReference,
            subscription.PlanHandle,
            targetPlanHandle,
            timing,
            current.ProratedAdjustment,
            timing == PlanChangeTiming.Immediate ? DateTimeOffset.UtcNow : updated.CurrentPeriodEndsAt), cancellationToken);

        return updated;
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionAsync(string userReference,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming = CancellationTiming.Immediate,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetMySubscriptionAsync(userReference, subscriptionId, cancellationToken);
        return await ApplyLifecycleActionCoreAsync(subscription, userReference, action, cancellationTiming, reason, cancellationToken);
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionForSubscriptionAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming = CancellationTiming.Immediate,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        return await ApplyLifecycleActionCoreAsync(subscription,
            subscription.CustomerReference,
            action,
            cancellationTiming,
            reason,
            cancellationToken);
    }

    private async Task<UsageSummary> RecordUsageCoreAsync(CustomerSubscription subscription,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        var recorded = await _billingClient.RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);

        // The usage is billed the moment the provider accepts it. Reading the running total back is
        // a convenience, so a failure there is reported as "total unavailable" rather than being
        // allowed to fail an operation that already succeeded (UC2).
        try
        {
            var total = await _billingClient.GetUsageTotalAsync(subscription.Id,
                subscription.CurrentPeriodStartedAt,
                cancellationToken);
            var unitPrice = await _billingClient.GetUsageUnitPriceAsync(cancellationToken);

            return UsageSummary.WithTotal(recorded,
                total,
                unitPrice,
                subscription.CurrentPeriodStartedAt,
                subscription.CurrentPeriodEndsAt);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Usage {0} was recorded on subscription {1}, but the period-to-date total could not be read: {2}",
                recorded.Id, subscription.Id, ex.ProviderMessage);
            return UsageSummary.WithoutTotal(recorded);
        }
    }

    private async Task<CustomerSubscription> ApplyLifecycleActionCoreAsync(CustomerSubscription subscription,
        string userReference,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        // Reject an illegal transition locally, with the legal alternatives, and make no provider
        // call at all (UC4).
        if (!subscription.CanApply(action, cancellationTiming))
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id,
                action,
                subscription.State,
                subscription.AllowedActions);
        }

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscription.Id, null, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscription.Id, cancellationTiming, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscription.Id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
        };

        var effectiveAt = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
            ? updated.ScheduledCancellationAt ?? updated.CurrentPeriodEndsAt
            : DateTimeOffset.UtcNow;

        await PublishAsync(new SubscriptionStateChanged(subscription.Id,
            userReference,
            action,
            subscription.State,
            updated.State,
            effectiveAt), cancellationToken);

        return updated;
    }

    private static bool OwnedBy(CustomerSubscription subscription, string userReference) =>
        string.Equals(subscription.CustomerReference, userReference, StringComparison.OrdinalIgnoreCase);

    private static void EnsurePlanChangeAllowed(CustomerSubscription subscription, string targetPlanHandle)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw PlanChangeNotAllowedException.SamePlan(subscription.Id, targetPlanHandle);
        }

        if (!subscription.IsLive)
        {
            throw PlanChangeNotAllowedException.WrongState(subscription.Id, subscription.State);
        }
    }

    /// <summary>
    /// Publishes a lifecycle notification best-effort. eShopOnWeb has no broker and no outbox, so a
    /// handler that throws is logged and swallowed: the billing action has already succeeded and is
    /// never rolled back because an in-process subscriber failed (plan.md §2.5).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process publication of {0} failed after the billing action succeeded: {1}",
                notification.GetType().Name, ex.Message);
        }
    }
}
