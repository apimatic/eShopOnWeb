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
/// Orchestrates eShopOnWeb's subscription use cases over the provider-agnostic billing seam,
/// mirroring <see cref="OrderService"/>: validate, call the provider, announce the result.
/// Notifications are published best-effort after the provider call has already succeeded, so a
/// failing handler can never undo a real billing change (plan section 2.5).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IMeteredComponentValidator _meteredComponentValidator;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient,
        IMeteredComponentValidator meteredComponentValidator,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _meteredComponentValidator = meteredComponentValidator;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(
        CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<BillingSubscription> SubscribeAsync(string userReference,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: an unresolvable handle is an operator problem.
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"The plan '{planHandle}' does not exist on the configured product family. " +
                "Re-run the billing provider seed, or correct the configured plan handles.");
        }

        var customer = await _billingClient.EnsureCustomerAsync(userReference,
            userReference,
            firstName: null,
            lastName: null,
            cancellationToken);

        // A repeated subscribe (double-click, retried request) must never create a second
        // enrollment — return what the customer already has.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(subscription => subscription.IsLive);
        if (live is not null)
        {
            _logger.LogInformation(
                $"User {userReference} already has live subscription {live.Id} on plan {live.PlanHandle}; " +
                "returning it instead of enrolling again.");
            return live;
        }

        var created = await _billingClient.CreateSubscriptionAsync(userReference, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userReference, created),
            $"activation of subscription {created.Id}",
            cancellationToken);

        return created;
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> GetSubscriptionsForUserAsync(
        string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<UsageRecord> RecordUsageAsync(string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        // Reject invalid quantities before anything is sent to the provider.
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity,
                "Usage quantity must be greater than zero.");
        }

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);
        if (!subscription.IsLive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "record usage against", "active or trialing");
        }

        var componentHandle = _billingClient.MeteredComponentHandle;
        await _meteredComponentValidator.EnsureComponentIsMeteredAsync(_billingClient, componentHandle,
            cancellationToken);

        var record = await _billingClient.RecordUsageAsync(subscription.Id, componentHandle, quantity, memo,
            cancellationToken);

        // The usage is already recorded. A failed read-back must not fail the whole operation —
        // report success with the running total marked unavailable instead.
        decimal? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetPeriodToDateUsageAsync(subscription.Id, componentHandle,
                cancellationToken);
        }
        catch (BillingProviderException exception)
        {
            _logger.LogWarning(
                $"Recorded {quantity} units against subscription {subscription.Id} but could not read back the " +
                $"period-to-date total: {exception.Message}");
            periodToDate = null;
        }

        return new UsageRecord(record.Id, record.SubscriptionId, record.ComponentHandle, record.Quantity)
        {
            Memo = record.Memo,
            PeriodToDateTotal = periodToDate
        };
    }

    public async Task<decimal?> GetPeriodToDateUsageAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);

        return await _billingClient.GetPeriodToDateUsageAsync(subscription.Id,
            _billingClient.MeteredComponentHandle, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference,
        string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, cancellationToken);
    }

    public async Task<BillingSubscription> ChangePlanAsync(string userReference,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal? expectedPaymentDue = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);

        var targetPlan = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"The plan '{targetPlanHandle}' does not exist on the configured product family. " +
                "Re-run the billing provider seed, or correct the configured plan handles.");
        }

        // Only an immediate change is prorated, so only an immediate change can go stale.
        if (expectedPaymentDue.HasValue && timing == PlanChangeTiming.Immediate)
        {
            var fresh = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle,
                cancellationToken);

            if (fresh.PaymentDue != expectedPaymentDue.Value)
            {
                throw new StalePlanChangePreviewException(expectedPaymentDue.Value, fresh.PaymentDue);
            }
        }

        var previousPlanHandle = subscription.PlanHandle;
        var updated = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(userReference, updated, previousPlanHandle, timing),
            $"plan change on subscription {updated.Id}",
            cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> PauseAsync(string userReference,
        DateTimeOffset? automaticallyResumeAt = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);
        if (!subscription.IsLive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "pause", "active or trialing");
        }

        return await ApplyLifecycleTransitionAsync(userReference, subscription, "pause",
            () => _billingClient.PauseAsync(subscription.Id, automaticallyResumeAt, cancellationToken),
            cancellationToken);
    }

    public async Task<BillingSubscription> ResumeAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);
        if (subscription.Status != SubscriptionStatus.OnHold)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "resume", "on hold");
        }

        return await ApplyLifecycleTransitionAsync(userReference, subscription, "resume",
            () => _billingClient.ResumeAsync(subscription.Id, cancellationToken),
            cancellationToken);
    }

    public async Task<BillingSubscription> CancelAsync(string userReference,
        CancellationTiming timing,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);

        if (subscription.Status is SubscriptionStatus.Canceled or SubscriptionStatus.Expired)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "cancel", "any state other than canceled or expired");
        }

        // An end-of-period cancel only makes sense while the subscription is still running.
        if (timing == CancellationTiming.EndOfBillingPeriod && !subscription.IsLive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "schedule an end-of-period cancellation for", "active or trialing");
        }

        return await ApplyLifecycleTransitionAsync(userReference, subscription, "cancel",
            () => _billingClient.CancelAsync(subscription.Id, timing, reason, cancellationToken),
            cancellationToken);
    }

    public async Task<BillingSubscription> ReactivateAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await RequireCurrentSubscriptionAsync(userReference, cancellationToken);

        if (subscription.Status is not (SubscriptionStatus.Canceled
            or SubscriptionStatus.TrialEnded
            or SubscriptionStatus.Unpaid))
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "reactivate", "canceled, trial ended or unpaid");
        }

        return await ApplyLifecycleTransitionAsync(userReference, subscription, "reactivate",
            () => _billingClient.ReactivateAsync(subscription.Id, cancellationToken),
            cancellationToken);
    }

    private async Task<BillingSubscription> ApplyLifecycleTransitionAsync(string userReference,
        BillingSubscription subscription,
        string action,
        Func<Task<BillingSubscription>> transition,
        CancellationToken cancellationToken)
    {
        var previousStatus = subscription.Status;
        var updated = await transition();

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(userReference, updated, previousStatus, action),
            $"{action} of subscription {updated.Id}",
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Returns the subscription eShopOnWeb manages for this user — the live one when there is
    /// one, otherwise the most recently created.
    /// </summary>
    private async Task<BillingSubscription> RequireCurrentSubscriptionAsync(string userReference,
        CancellationToken cancellationToken)
    {
        var subscriptions = await GetSubscriptionsForUserAsync(userReference, cancellationToken);

        var current = subscriptions
            .OrderByDescending(subscription => subscription.IsLive)
            .ThenByDescending(subscription => subscription.Id)
            .FirstOrDefault();

        if (current is null)
        {
            throw new NoActiveSubscriptionException(userReference);
        }

        return current;
    }

    private static void EnsurePlanChangeIsPossible(BillingSubscription subscription, string targetPlanHandle)
    {
        if (!subscription.IsLive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.Id, subscription.Status,
                "change the plan of", "active or trialing");
        }

        // A no-op change is rejected here so it never reaches the provider.
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderValidationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }
    }

    /// <summary>
    /// Publishes a lifecycle notification without letting a handler failure affect the billing
    /// change that already succeeded (plan section 2.5).
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                $"The {description} succeeded but publishing its notification failed: {exception.Message}");
        }
    }
}
