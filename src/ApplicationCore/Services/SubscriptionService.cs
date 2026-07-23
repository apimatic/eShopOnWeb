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
/// Orchestrates the subscription use cases: validate the request, drive <see cref="IBillingClient"/>,
/// then announce the outcome through MediatR (plan.md §4.2, mirroring <see cref="OrderService"/>).
/// </summary>
/// <remarks>
/// Notification publication is best-effort: once the provider has accepted a change, a failing handler is
/// logged and swallowed rather than rolling the change back (plan.md §2.5).
/// </remarks>
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

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(
        string userName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Never enrol against a guessed plan: the configured handle must resolve (UC1 failure scenario).
        var plan = await _billingClient.FindPlanAsync(planHandle, cancellationToken)
            ?? throw BillingConfigurationException.UnresolvedHandle("plan", planHandle);

        var registration = BillingCustomerRegistration.ForUser(userName);
        var customer = await _billingClient.EnsureCustomerAsync(registration, cancellationToken);

        // Idempotency: a repeated subscribe (double-click, retry) returns the live subscription rather
        // than creating a second enrolment (UC1, "duplicate subscribe" failure scenario).
        var existing = await _billingClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsLive);
        if (live is not null)
        {
            _logger.LogInformation(
                "Subscribe for {0} returned existing subscription {1} on plan {2}; no new enrolment created.",
                registration.Reference, live.Id, live.PlanHandle ?? "(unknown)");
            return live;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(
            customer.Id, plan.Handle, cancellationToken);

        await PublishAsync(
            new SubscriptionActivated(
                subscription.Id,
                subscription.CustomerReference ?? registration.Reference,
                subscription.PlanHandle ?? plan.Handle,
                subscription.PlanName ?? plan.Name,
                subscription.PlanPriceInCents == 0 ? plan.Price : subscription.PlanPrice,
                subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt),
            cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var customer = await _billingClient.FindCustomerAsync(userName.Trim(), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<Subscription> GetSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));

        return await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(
        SubscriptionActor actor,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));

        // Rejected before any provider call (UC2, "quantity is zero or negative").
        if (quantity <= 0m)
        {
            throw new InvalidUsageQuantityException(quantity);
        }

        var subscription = await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw NoActiveSubscriptionException.ForSubscription(subscription.Id, subscription.State.ToString());
        }

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport?> RecordUsageForUserAsync(
        string userName,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        if (quantity <= 0m)
        {
            throw new InvalidUsageQuantityException(quantity);
        }

        var customer = await _billingClient.FindCustomerAsync(userName.Trim(), cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var subscriptions = await _billingClient.ListSubscriptionsAsync(customer.Id, cancellationToken);
        var subscription = subscriptions.FirstOrDefault(s => s.IsActive);
        if (subscription is null)
        {
            return null;
        }

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> GetUsageSummaryAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));

        var subscription = await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);

        return new UsageReport
        {
            SubscriptionId = subscription.Id,
            ComponentHandle = component.Handle,
            PeriodToDateUnits = await ReadPeriodToDateAsync(subscription.Id, cancellationToken),
            UnitPriceInCents = component.UnitPriceInCents
        };
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);
        var targetPlan = await EnsurePlanChangeAllowedAsync(subscription, targetPlanHandle, cancellationToken);

        var preview = await _billingClient.PreviewPlanChangeAsync(
            subscription.Id, targetPlan.Handle, cancellationToken);

        return preview with
        {
            CurrentPlanHandle = subscription.PlanHandle,
            TargetPlanName = targetPlan.Name
        };
    }

    public async Task<Subscription> ChangePlanAsync(
        SubscriptionActor actor,
        int subscriptionId,
        PlanChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrWhiteSpace(request.TargetPlanHandle, nameof(request.TargetPlanHandle));

        var subscription = await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);
        var targetPlan = await EnsurePlanChangeAllowedAsync(
            subscription, request.TargetPlanHandle, cancellationToken);

        Subscription updated;
        decimal? paymentDue = null;

        if (request.Timing == PlanChangeTiming.Immediately)
        {
            var confirmed = EnsurePreviewIsFresh(request);

            // Re-price at commit time; never apply an amount other than the one the customer confirmed
            // (UC3, "preview is stale at commit time").
            var current = await _billingClient.PreviewPlanChangeAsync(
                subscription.Id, targetPlan.Handle, cancellationToken);

            if (current.PaymentDueInCents != confirmed)
            {
                throw StalePlanChangePreviewException.AmountChanged(confirmed, current.PaymentDueInCents);
            }

            paymentDue = current.PaymentDue;
            updated = await _billingClient.ChangePlanImmediatelyAsync(
                subscription.Id, targetPlan.Handle, cancellationToken);
        }
        else
        {
            updated = await _billingClient.SchedulePlanChangeAsync(
                subscription.Id, targetPlan.Handle, cancellationToken);
        }

        await PublishAsync(
            new SubscriptionPlanChanged(
                updated.Id,
                updated.CustomerReference ?? actor.UserName,
                subscription.PlanHandle,
                targetPlan.Handle,
                request.Timing,
                paymentDue,
                request.Timing == PlanChangeTiming.Immediately
                    ? DateTimeOffset.UtcNow
                    : updated.CurrentPeriodEndsAt ?? updated.NextAssessmentAt),
            cancellationToken);

        return updated;
    }

    public async Task<Subscription> ExecuteLifecycleActionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        SubscriptionLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.Null(request, nameof(request));

        var subscription = await LoadAuthorizedAsync(actor, subscriptionId, cancellationToken);

        // Illegal transitions are rejected locally, with no provider call (UC4 failure scenario).
        SubscriptionLifecyclePolicy.EnsureAllowed(subscription, request.Action);

        var updated = request.Action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscription.Id, request.Reason, cancellationToken),
            SubscriptionLifecycleAction.CancelAtEndOfPeriod =>
                await _billingClient.CancelSubscriptionAtPeriodEndAsync(
                    subscription.Id, request.Reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscription.Id, cancellationToken),
            _ => throw new InvalidSubscriptionTransitionException(
                subscription.Id,
                subscription.State,
                request.Action,
                SubscriptionLifecyclePolicy.AllowedActions(subscription))
        };

        await PublishAsync(
            new SubscriptionStateChanged(
                updated.Id,
                updated.CustomerReference ?? actor.UserName,
                subscription.State,
                updated.State,
                request.Action,
                EffectiveDateFor(request.Action, updated)),
            cancellationToken);

        return updated;
    }

    private async Task<UsageReport> RecordUsageCoreAsync(
        Subscription subscription,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        // Refuse to record against a component that is not metered (UC2 preconditions). Resolving the
        // component also gives the unit price used to estimate the accruing charge.
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"The configured usage component '{component.Handle}' is of kind " +
                $"'{component.Kind ?? "unknown"}', not metered, so usage cannot be recorded. " +
                "Re-seed the provider sandbox with a metered component (see plan.md UC0).");
        }

        var record = await _billingClient.RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);

        return new UsageReport
        {
            SubscriptionId = subscription.Id,
            ComponentHandle = component.Handle,
            Record = record,
            PeriodToDateUnits = await ReadPeriodToDateAsync(subscription.Id, cancellationToken),
            UnitPriceInCents = component.UnitPriceInCents
        };
    }

    /// <summary>
    /// Reads the running total, degrading to "unavailable" instead of failing the whole operation
    /// (UC2, "read-back of the running total fails after a successful record").
    /// </summary>
    private async Task<decimal?> ReadPeriodToDateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetPeriodToDateUsageAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Period-to-date usage for subscription {0} could not be read back: {1}",
                subscriptionId, ex.Message);
            return null;
        }
    }

    private async Task<Subscription> LoadAuthorizedAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        if (!actor.CanAct(subscription.CustomerReference))
        {
            throw new SubscriptionAccessDeniedException(subscriptionId);
        }

        return subscription;
    }

    private async Task<SubscriptionPlan> EnsurePlanChangeAllowedAsync(
        Subscription subscription,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        var target = targetPlanHandle.Trim();

        // A no-op change is rejected before any provider call (UC3 failure scenario).
        if (string.Equals(subscription.PlanHandle, target, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidPlanChangeException.SamePlan(target);
        }

        if (!subscription.IsActive && subscription.State != SubscriptionState.PastDue)
        {
            throw new InvalidPlanChangeException(
                $"Subscription {subscription.Id} is {subscription.State} and cannot change plan. " +
                "Reactivate it first, then try again.");
        }

        return await _billingClient.FindPlanAsync(target, cancellationToken)
            ?? throw BillingConfigurationException.UnresolvedHandle("plan", target);
    }

    private static long EnsurePreviewIsFresh(PlanChangeRequest request)
    {
        if (request.ConfirmedPaymentDueInCents is not { } confirmed || request.PreviewedAt is not { } previewedAt)
        {
            throw StalePlanChangePreviewException.Missing();
        }

        if (DateTimeOffset.UtcNow - previewedAt > SubscriptionConstants.PreviewValidity)
        {
            throw StalePlanChangePreviewException.Expired();
        }

        return confirmed;
    }

    private static DateTimeOffset? EffectiveDateFor(SubscriptionLifecycleAction action, Subscription updated) =>
        action switch
        {
            SubscriptionLifecycleAction.CancelAtEndOfPeriod =>
                updated.ScheduledCancellationAt ?? updated.CurrentPeriodEndsAt,
            SubscriptionLifecycleAction.Cancel => updated.CanceledAt ?? DateTimeOffset.UtcNow,
            _ => DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Publishes an in-process notification. Delivery is best-effort: once the provider has accepted the
    /// change, a failing handler is logged and swallowed rather than rolling the change back (§2.5).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "In-process handling of {0} failed after the billing change was applied: {1}",
                notification.GetType().Name, ex.Message);
        }
    }
}
