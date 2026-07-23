using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Orchestrates the subscription use cases: validate the request, drive the billing client, then
/// publish the matching in-process notification.
/// </summary>
/// <remarks>
/// Per §2.5 eventing is best-effort: a notification handler that fails never rolls back a change the
/// provider has already applied.
/// </remarks>
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

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // UC1 step 2 — the plan must resolve before anything is created provider-side.
        var plan = await _billingClient.FindPlanAsync(planHandle, cancellationToken)
            ?? throw new BillingProviderException(
                $"Plan '{planHandle}' does not resolve on the billing provider. Check the configured product handles (UC0).");

        // UC1 step 3 — idempotent on the user reference.
        var customer = await _billingClient.EnsureCustomerAsync(
            CustomerRegistration.FromUserReference(userReference), cancellationToken);

        // UC1 failure scenario — never create a second enrollment for a customer who already has one.
        var existing = await _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive);
        if (alreadyActive is not null)
        {
            // A repeat of the same request (double-click, retry) is idempotent...
            if (string.Equals(alreadyActive.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Customer {0} (provider id {1}) already has active subscription {2} on plan {3}; returning it instead of enrolling again.",
                    userReference,
                    customer.Id,
                    alreadyActive.Id,
                    alreadyActive.PlanHandle);

                return alreadyActive;
            }

            // ...but asking for a *different* plan is a plan change, and silently returning the old
            // subscription would report a success that never happened.
            throw new InvalidSubscriptionOperationException(
                $"'{userReference}' already has an active subscription on plan '{alreadyActive.PlanHandle}'. " +
                $"Change that subscription to '{plan.Handle}' instead of enrolling again.");
        }

        // UC1 step 4.
        var subscription = await _billingClient.CreateSubscriptionAsync(userReference, plan.Handle, cancellationToken);

        // UC1 step 6 — best-effort.
        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        return _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return null;
        }

        return IsOwnedBy(subscription, ownerReference) ? subscription : null;
    }

    public async Task<UsageReport> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userReference, cancellationToken);
        var active = subscriptions.FirstOrDefault(s => s.IsActive)
            ?? throw new InvalidSubscriptionOperationException(
                $"'{userReference}' has no active subscription to record usage against.");

        return await RecordUsageCoreAsync(active, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerReference, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport?> GetUsageSummaryAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
        var periodToDate = await ReadPeriodToDateAsync(subscription.Id, cancellationToken);

        var placeholder = new UsageRecord(0, subscription.Id, component.Id, component.Handle, 0m, null, null);

        return new UsageReport(placeholder, periodToDate, component.UnitPrice);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string? ownerReference,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        ValidatePlanChange(subscription, targetPlanHandle);

        // UC3 failure scenario — an unresolvable or archived target plan is a configuration error.
        _ = await _billingClient.FindPlanAsync(targetPlanHandle, cancellationToken)
            ?? throw new BillingProviderException(
                $"Target plan '{targetPlanHandle}' does not resolve on the billing provider. Check the configured product handles (UC0).");

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId,
        string? ownerReference,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewToken,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));
        Guard.Against.NullOrWhiteSpace(previewToken, nameof(previewToken));

        var subscription = await RequireSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        ValidatePlanChange(subscription, targetPlanHandle);

        // UC3 failure scenario — never apply an amount other than the one the customer was shown.
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
        if (!string.Equals(freshPreview.Token, previewToken, StringComparison.Ordinal))
        {
            throw new InvalidSubscriptionOperationException(
                "The plan change preview is no longer current — the price or proration basis changed. Review a fresh preview before confirming.");
        }

        var previousPlanHandle = subscription.PlanHandle;
        var updated = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(updated, previousPlanHandle, freshPreview), cancellationToken);

        return updated;
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId,
        string? ownerReference,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        // UC4 failure scenario — an illegal transition makes no provider call at all.
        if (!IsTransitionLegal(subscription.State, action))
        {
            throw new InvalidSubscriptionOperationException(
                $"Cannot {action.ToString().ToLowerInvariant()} subscription {subscriptionId} while it is {subscription.State}. " +
                $"Legal actions from this state: {DescribeLegalActions(subscription.State)}.");
        }

        var previousState = subscription.State;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelAsync(subscriptionId, cancellationTiming, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateAsync(subscriptionId, cancellationToken),
            _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
        };

        await PublishAsync(new SubscriptionStateChanged(updated, previousState, action), cancellationToken);

        return updated;
    }

    /// <summary>
    /// The UC4 transition table. Deliberately conservative: an <see cref="SubscriptionState.Unknown"/>
    /// state permits nothing, because guessing risks applying a transition the provider will reject.
    /// </summary>
    internal static bool IsTransitionLegal(SubscriptionState state, SubscriptionLifecycleAction action) => action switch
    {
        SubscriptionLifecycleAction.Pause => state is SubscriptionState.Active or SubscriptionState.Trialing
            or SubscriptionState.Assessing or SubscriptionState.PastDue or SubscriptionState.SoftFailure,
        SubscriptionLifecycleAction.Resume => state is SubscriptionState.Paused or SubscriptionState.OnHold,
        SubscriptionLifecycleAction.Cancel => state is SubscriptionState.Active or SubscriptionState.Trialing
            or SubscriptionState.Assessing or SubscriptionState.PastDue or SubscriptionState.SoftFailure
            or SubscriptionState.Paused or SubscriptionState.OnHold or SubscriptionState.Unpaid
            or SubscriptionState.TrialEnded or SubscriptionState.Suspended,
        SubscriptionLifecycleAction.Reactivate => state is SubscriptionState.Canceled or SubscriptionState.Expired
            or SubscriptionState.TrialEnded,
        _ => false
    };

    private static string DescribeLegalActions(SubscriptionState state)
    {
        var legal = Enum.GetValues<SubscriptionLifecycleAction>()
            .Where(a => IsTransitionLegal(state, a))
            .Select(a => a.ToString().ToLowerInvariant())
            .ToArray();

        return legal.Length == 0 ? "none" : string.Join(", ", legal);
    }

    private static void ValidatePlanChange(Subscription subscription, string targetPlanHandle)
    {
        // UC3 failure scenario — same plan is a no-op, rejected before any provider call.
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        // UC3 failure scenario — a cancelled subscription must be reactivated first.
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is {subscription.State} and cannot change plan. Reactivate it first.");
        }
    }

    private async Task<UsageReport> RecordUsageCoreAsync(Subscription subscription,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        // UC2 failure scenario — invalid quantity is rejected before any provider call.
        if (quantity <= 0m)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero (got {quantity.ToString(CultureInfo.InvariantCulture)}).");
        }

        // UC2 failure scenario — usage only accrues to a live subscription.
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is {subscription.State}; usage can only be recorded against an active subscription.");
        }

        // UC2 precondition — refuse to record usage unless the configured component is truly metered.
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
        if (!component.IsMetered)
        {
            throw new BillingProviderException(
                $"Component '{component.Handle}' is of kind '{component.Kind}', not metered. Correct the product-family seed (UC0) before recording usage.");
        }

        var record = await _billingClient.RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);

        // UC2 failure scenario — a failed read-back does not fail the operation.
        var periodToDate = await ReadPeriodToDateAsync(subscription.Id, cancellationToken);

        return new UsageReport(record, periodToDate, component.UnitPrice);
    }

    private async Task<int?> ReadPeriodToDateAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetPeriodToDateUsageAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Recorded usage for subscription {0} but could not read the period-to-date total: {1}",
                subscriptionId,
                ex.Message);

            return null;
        }
    }

    private async Task<Subscription> RequireSubscriptionAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} was not found.");

        if (!IsOwnedBy(subscription, ownerReference))
        {
            // Same message as "not found" on purpose: an unauthorized caller learns nothing about
            // whether the subscription exists.
            throw new InvalidSubscriptionOperationException($"Subscription {subscriptionId} was not found.");
        }

        return subscription;
    }

    private static bool IsOwnedBy(Subscription subscription, string? ownerReference) =>
        ownerReference is null ||
        string.Equals(subscription.CustomerReference, ownerReference, StringComparison.OrdinalIgnoreCase);

    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // §2.5 — eventing is best-effort. The provider-side change already stands.
            _logger.LogWarning(
                "In-process notification {0} failed after the billing operation succeeded: {1}",
                notification.GetType().Name,
                ex.Message);
        }
    }
}
