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
/// Orchestrates the subscription use cases over the provider-agnostic billing seam. eShopOnWeb keeps
/// no local copy of the subscription: the billing provider is the system of record and the customer
/// reference (the signed-in user's email/username) is the stable link between the two, which is what
/// makes repeated subscribe calls idempotent.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly SubscriptionLifecycleState[] PausableStates =
    {
        SubscriptionLifecycleState.Active, SubscriptionLifecycleState.Trialing
    };

    private static readonly SubscriptionLifecycleState[] ResumableStates =
    {
        SubscriptionLifecycleState.Paused
    };

    private static readonly SubscriptionLifecycleState[] CancellableStates =
    {
        SubscriptionLifecycleState.Active, SubscriptionLifecycleState.Trialing,
        SubscriptionLifecycleState.PastDue, SubscriptionLifecycleState.Paused,
        SubscriptionLifecycleState.Unpaid, SubscriptionLifecycleState.Suspended
    };

    private static readonly SubscriptionLifecycleState[] ReactivatableStates =
    {
        SubscriptionLifecycleState.Canceled, SubscriptionLifecycleState.Expired,
        SubscriptionLifecycleState.TrialEnded, SubscriptionLifecycleState.Unpaid
    };

    private static readonly SubscriptionLifecycleState[] PlanChangeableStates =
    {
        SubscriptionLifecycleState.Active, SubscriptionLifecycleState.Trialing
    };

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

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
        => _billingClient.GetMeteredComponentAsync(cancellationToken);

    public async Task<CustomerSubscription> SubscribeAsync(string userName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Resolves the configured handle against the live catalog, so a re-seeded sandbox fails as a
        // configuration error instead of enrolling anyone against a guessed plan.
        var plan = await _billingClient.GetPlanAsync(planHandle, cancellationToken);

        var (firstName, lastName) = SplitDisplayName(userName);
        await _billingClient.EnsureCustomerAsync(userName, firstName, lastName, userName, cancellationToken);

        // A repeated subscribe (double click, retried request) must never enroll twice.
        var existing = await _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsBillable);
        if (live is not null)
        {
            _logger.LogInformation(
                "{0} already holds subscription {1} on plan {2}; returning it instead of enrolling again.",
                userName, live.Id, live.PlanHandle ?? "unknown");
            return live;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(userName, plan.Handle, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionActivated(
                subscription.Id,
                userName,
                subscription.PlanHandle ?? plan.Handle,
                subscription.PlanName ?? plan.Name,
                subscription.PlanPrice == 0 ? plan.Price : subscription.PlanPrice,
                subscription.NextBillingDate),
            cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        return _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
    }

    public async Task<CustomerSubscription?> GetActiveSubscriptionAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsBillable);
    }

    public async Task<UsageSummary> RecordUsageAsync(string userName,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var subscription = await GetActiveSubscriptionAsync(userName, cancellationToken)
            ?? throw new SubscriptionNotFoundException(userName);

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageSummary> RecordUsageForSubscriptionAsync(int subscriptionId,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<int?> GetPeriodToDateUsageAsync(string userName,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveOwnedSubscriptionAsync(userName, subscriptionId, cancellationToken);

        return await _billingClient.GetPeriodToDateUsageAsync(subscription.Id, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userName,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await ResolveOwnedSubscriptionAsync(userName, subscriptionId, cancellationToken);
        GuardPlanChangeIsAllowed(subscription, targetPlanHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(string userName,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string? confirmedFingerprint,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await ResolveOwnedSubscriptionAsync(userName, subscriptionId, cancellationToken);
        GuardPlanChangeIsAllowed(subscription, targetPlanHandle);

        // Re-price at commit time. If the basis moved since the customer was shown a number, refuse
        // rather than charging an amount they never confirmed.
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);
        if (!string.IsNullOrEmpty(confirmedFingerprint) &&
            !string.Equals(confirmedFingerprint, freshPreview.Fingerprint, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Refusing plan change on subscription {0}: the confirmed preview no longer matches the current basis.",
                subscription.Id);
            throw new StalePlanChangePreviewException(subscription.Id);
        }

        var previousPlanHandle = subscription.PlanHandle ?? string.Empty;
        var updated = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);

        var result = new PlanChangeResult(updated, previousPlanHandle, targetPlanHandle, timing, freshPreview);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(
                updated.Id,
                userName,
                previousPlanHandle,
                targetPlanHandle,
                result.ProrationAmount,
                timing,
                result.EffectiveAt),
            cancellationToken);

        return result;
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionAsync(string userName,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await ResolveOwnedSubscriptionAsync(userName, subscriptionId, cancellationToken);

        return await ApplyLifecycleCoreAsync(subscription, userName, action, cancellationTiming, reason, cancellationToken);
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionForSubscriptionAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        return await ApplyLifecycleCoreAsync(subscription, subscription.CustomerReference, action, cancellationTiming, reason, cancellationToken);
    }

    private async Task<UsageSummary> RecordUsageCoreAsync(CustomerSubscription subscription,
        int quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        // Invalid quantities are rejected before anything leaves the process.
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        if (!subscription.IsBillable)
        {
            throw new SubscriptionNotBillableException(subscription.Id, subscription.State);
        }

        // The component must exist and be metered before a single unit is reported. The billing client
        // enforces this too; re-checking here means no provider implementation can let a non-metered
        // component through and produce a confusing failure at usage time.
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(nameof(RecordUsageAsync),
                $"component '{component.Handle}' is of kind '{component.Kind}', not metered, so usage cannot be recorded.");
        }

        var receipt = await _billingClient.RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);

        // A failed read-back of the running total must not fail an operation that already succeeded.
        int? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetPeriodToDateUsageAsync(subscription.Id, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Usage was recorded on subscription {0} but the period-to-date total could not be read: {1}",
                subscription.Id, ex.Message);
            periodToDate = null;
        }

        return new UsageSummary(receipt, periodToDate, component.UnitPrice);
    }

    private async Task<CustomerSubscription> ApplyLifecycleCoreAsync(CustomerSubscription subscription,
        string? userName,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        GuardTransitionIsLegal(subscription, action);

        var previousState = subscription.State;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelSubscriptionAsync(subscription.Id, cancellationTiming, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(subscription.Id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
        };

        var effectiveAt = action == SubscriptionLifecycleAction.Cancel && cancellationTiming == CancellationTiming.EndOfPeriod
            ? updated.DelayedCancelAt ?? updated.CurrentPeriodEndsAt
            : null;

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(updated.Id, userName, action, previousState, updated.State, effectiveAt),
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Loads the subscription and proves it belongs to this eShopOnWeb user. A subscription the user
    /// does not own is reported exactly like one that does not exist, so the endpoint cannot be used to
    /// probe for other customers' subscription ids.
    /// </summary>
    private async Task<CustomerSubscription> ResolveOwnedSubscriptionAsync(string userName,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userName, cancellationToken);

        return subscriptions.FirstOrDefault(s => s.Id == subscriptionId)
            ?? throw new SubscriptionNotFoundException(subscriptionId);
    }

    private static void GuardPlanChangeIsAllowed(CustomerSubscription subscription, string targetPlanHandle)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.Ordinal))
        {
            throw new PlanChangeNotAllowedException(subscription.Id,
                $"it is already on plan '{targetPlanHandle}'");
        }

        if (!PlanChangeableStates.Contains(subscription.State))
        {
            throw new PlanChangeNotAllowedException(subscription.Id,
                $"it is {subscription.State}; reactivate it before changing plan");
        }
    }

    private static void GuardTransitionIsLegal(CustomerSubscription subscription, SubscriptionLifecycleAction action)
    {
        var isLegal = action switch
        {
            SubscriptionLifecycleAction.Pause => PausableStates.Contains(subscription.State),
            SubscriptionLifecycleAction.Resume => ResumableStates.Contains(subscription.State),
            SubscriptionLifecycleAction.Cancel => CancellableStates.Contains(subscription.State),
            // Reactivate also covers revoking a cancellation that is still pending at period end.
            SubscriptionLifecycleAction.Reactivate => ReactivatableStates.Contains(subscription.State) || subscription.CancelAtEndOfPeriod,
            _ => false
        };

        if (!isLegal)
        {
            throw new InvalidSubscriptionTransitionException(
                subscription.Id,
                subscription.State,
                action,
                DescribeLegalActions(subscription));
        }
    }

    private static string DescribeLegalActions(CustomerSubscription subscription)
    {
        var legal = new List<string>();
        if (PausableStates.Contains(subscription.State)) legal.Add(nameof(SubscriptionLifecycleAction.Pause));
        if (ResumableStates.Contains(subscription.State)) legal.Add(nameof(SubscriptionLifecycleAction.Resume));
        if (CancellableStates.Contains(subscription.State)) legal.Add(nameof(SubscriptionLifecycleAction.Cancel));
        if (ReactivatableStates.Contains(subscription.State) || subscription.CancelAtEndOfPeriod) legal.Add(nameof(SubscriptionLifecycleAction.Reactivate));

        return legal.Count == 0 ? "none" : string.Join(", ", legal);
    }

    /// <summary>
    /// Publishes in-process, best effort. There is no durable outbox here by design, so a handler
    /// failure is logged and swallowed: the provider-side change has already succeeded and must stand.
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
                "In-process publication of {0} failed after the billing change succeeded: {1}",
                notification.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Derives a display name for the billing customer record from the eShopOnWeb username, which is an
    /// email address. The provider requires both name parts; the reference (the username itself) is
    /// what actually identifies the customer.
    /// </summary>
    private static (string FirstName, string LastName) SplitDisplayName(string userName)
    {
        var localPart = userName.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var first = segments.Length > 0 ? Capitalize(segments[0]) : localPart;
        var last = segments.Length > 1 ? Capitalize(segments[^1]) : "eShopOnWeb";

        return (string.IsNullOrWhiteSpace(first) ? "eShopOnWeb" : first, last);
    }

    private static string Capitalize(string value)
        => value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
