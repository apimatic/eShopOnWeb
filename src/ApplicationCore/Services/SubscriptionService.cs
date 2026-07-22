using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases (plan §3) exactly the way <see cref="OrderService"/>
/// orchestrates checkout: validate, drive the provider seam, then announce the change in-process.
/// </summary>
/// <remarks>
/// The billing provider is the system of record. Nothing here caches or mutates a local copy of a
/// subscription, so an out-of-band change at the provider is always reflected on the next read.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// eShopOnWeb Identity stores no personal name, but the provider requires a surname on the
    /// customer record. This constant marks the record's origin instead of inventing a name.
    /// </summary>
    private const string CustomerSurname = "eShopOnWeb";

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<BillingSubscription> SubscribeAsync(SubscriptionActor actor, string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reference = RequireUserReference(actor);
        planHandle = RequirePlanHandle(planHandle);

        var plan = await RequireSelectablePlanAsync(planHandle, cancellationToken);

        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken)
            ?? await CreateCustomerAsync(reference, cancellationToken);

        // Idempotency (UC1): a repeated subscribe must never produce a second enrolment.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadyEnrolled = existing.FirstOrDefault(s => s.IsLive && PlanMatches(s, planHandle))
            ?? existing.FirstOrDefault(s => s.IsLive && PlanMatches(s, plan.Handle))
            ?? existing.FirstOrDefault(s => s.IsLive);

        if (alreadyEnrolled is not null)
        {
            _logger.LogInformation(
                "Customer reference {UserReference} already holds live subscription {SubscriptionId}; returning it instead of enrolling again.",
                reference, alreadyEnrolled.Id);
            return alreadyEnrolled;
        }

        // The caller's own identifier is what travels to the provider; the catalog lookup above only
        // proves the plan is selectable.
        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(reference, subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(SubscriptionActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reference = RequireUserReference(actor);

        var customer = await _billingClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(SubscriptionActor actor, int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireUserReference(actor);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        await EnsureAccessAsync(actor, subscription, cancellationToken);
        return subscription;
    }

    public async Task<UsageReport> RecordUsageAsync(SubscriptionActor actor, int subscriptionId, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireUserReference(actor);

        if (quantity <= 0m)
        {
            throw new InvalidBillingRequestException(
                "Usage quantity must be greater than zero.");
        }

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);

        if (subscription.State is not (BillingSubscriptionState.Active or BillingSubscriptionState.Trialing))
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage can only be reported against an active subscription; subscription {subscriptionId} is {subscription.State}.",
                subscriptionId, subscription.State);
        }

        // UC2 precondition: refuse to meter until the configured component is proven to be metered.
        var component = await _billingClient.GetUsageComponentAsync(cancellationToken);

        var record = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

        decimal? periodToDate = null;
        var periodToDateAvailable = false;
        try
        {
            periodToDate = await _billingClient.GetPeriodToDateUsageAsync(subscriptionId, component.Id, cancellationToken);
            periodToDateAvailable = periodToDate.HasValue;
        }
        catch (BillingProviderException ex)
        {
            // The usage stands. Reporting the running total is best effort (UC2 failure scenarios).
            _logger.LogWarning(
                "Usage was recorded on subscription {SubscriptionId} but the period-to-date total could not be read: {Reason}",
                subscriptionId, ex.Message);
        }

        var estimatedAmount = periodToDate.HasValue ? periodToDate.Value * component.UnitPrice : (decimal?)null;

        return new UsageReport(record, periodToDate, component.UnitPrice, estimatedAmount, periodToDateAvailable);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(SubscriptionActor actor, int subscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireUserReference(actor);
        targetPlanHandle = RequirePlanHandle(targetPlanHandle);

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);
        var targetPlan = await RequirePlanChangeIsLegalAsync(subscription, targetPlanHandle, cancellationToken);

        return await QuoteAsync(subscription, targetPlanHandle, targetPlan, timing, cancellationToken);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(SubscriptionActor actor, int subscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, decimal? previewedPaymentDue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reference = RequireUserReference(actor);
        targetPlanHandle = RequirePlanHandle(targetPlanHandle);

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);
        var targetPlan = await RequirePlanChangeIsLegalAsync(subscription, targetPlanHandle, cancellationToken);
        var previousPlanHandle = subscription.PlanHandle ?? string.Empty;

        var quote = await QuoteAsync(subscription, targetPlanHandle, targetPlan, timing, cancellationToken);

        // UC3: never apply an amount other than the one the customer was shown.
        if (previewedPaymentDue.HasValue && previewedPaymentDue.Value != quote.PaymentDue)
        {
            throw new InvalidSubscriptionOperationException(
                $"The plan change preview is stale: it quoted {previewedPaymentDue.Value} but the current quote is {quote.PaymentDue}. Take a fresh preview and confirm again.",
                subscriptionId, subscription.State);
        }

        var updated = timing == PlanChangeTiming.Immediate
            ? await _billingClient.MigratePlanAsync(subscriptionId, targetPlanHandle, cancellationToken)
            : await _billingClient.SchedulePlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);

        var effectiveAt = timing == PlanChangeTiming.Immediate ? null : updated.CurrentPeriodEndsAt;

        await PublishAsync(new SubscriptionPlanChanged(reference, subscriptionId, previousPlanHandle,
            targetPlanHandle, timing, quote.PaymentDue), cancellationToken);

        return new PlanChangeResult(updated, previousPlanHandle, targetPlanHandle, timing, quote.PaymentDue, effectiveAt);
    }

    public async Task<SubscriptionLifecycleResult> ApplyLifecycleActionAsync(SubscriptionActor actor, int subscriptionId,
        SubscriptionLifecycleAction action, SubscriptionCancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reference = RequireUserReference(actor);

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);
        var previousState = subscription.State;

        EnsureTransitionIsLegal(subscription, action);

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel when cancellationTiming == SubscriptionCancellationTiming.EndOfPeriod =>
                await _billingClient.ScheduleCancellationAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            _ => throw new InvalidBillingRequestException($"Unsupported lifecycle action '{action}'.")
        };

        var effectiveAt = action == SubscriptionLifecycleAction.Cancel
            && cancellationTiming == SubscriptionCancellationTiming.EndOfPeriod
                ? updated.ScheduledCancellationAt ?? updated.CurrentPeriodEndsAt
                : null;

        var message = action == SubscriptionLifecycleAction.Cancel
            && cancellationTiming == SubscriptionCancellationTiming.EndOfPeriod
            && !updated.CancelAtEndOfPeriod
                ? "The provider did not report a pending end-of-period cancellation for this subscription."
                : null;

        await PublishAsync(new SubscriptionStateChanged(reference, subscriptionId, action, previousState,
            updated.State), cancellationToken);

        return new SubscriptionLifecycleResult(updated, previousState, updated.State, action, effectiveAt, message);
    }

    /// <summary>
    /// Quotes a change to <paramref name="targetPlanIdentifier"/>. The caller's own identifier is what
    /// travels to the provider and what is echoed back — the catalog lookup only proves the plan is
    /// selectable and supplies its price.
    /// </summary>
    private async Task<PlanChangePreview> QuoteAsync(BillingSubscription subscription, string targetPlanIdentifier,
        BillingPlan targetPlan, PlanChangeTiming timing, CancellationToken cancellationToken)
    {
        var currentPlanHandle = subscription.PlanHandle ?? string.Empty;

        if (timing == PlanChangeTiming.NextRenewal)
        {
            // Deferred changes are never prorated: the new plan price simply applies from the next period.
            return new PlanChangePreview(currentPlanHandle, targetPlanIdentifier, timing,
                ProratedAdjustment: 0m, Charge: 0m, PaymentDue: 0m, CreditApplied: 0m, targetPlan.Price);
        }

        var quote = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanIdentifier,
            cancellationToken);

        return new PlanChangePreview(currentPlanHandle, targetPlanIdentifier, timing, quote.ProratedAdjustment,
            quote.Charge, quote.PaymentDue, quote.CreditApplied, targetPlan.Price);
    }

    private async Task<BillingPlan> RequirePlanChangeIsLegalAsync(BillingSubscription subscription,
        string targetPlanHandle, CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidBillingRequestException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.AllowsPlanChange)
        {
            throw new InvalidSubscriptionOperationException(
                $"A plan change requires an active subscription; subscription {subscription.Id} is {subscription.State}.",
                subscription.Id, subscription.State);
        }

        return await RequireSelectablePlanAsync(targetPlanHandle, cancellationToken);
    }

    private static void EnsureTransitionIsLegal(BillingSubscription subscription, SubscriptionLifecycleAction action)
    {
        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => subscription.State is BillingSubscriptionState.Active
                or BillingSubscriptionState.Trialing,
            SubscriptionLifecycleAction.Resume => subscription.IsPaused,
            SubscriptionLifecycleAction.Cancel => !subscription.IsTerminated,
            SubscriptionLifecycleAction.Reactivate => !subscription.IsPaused
                && subscription.State is not (BillingSubscriptionState.Active or BillingSubscriptionState.Trialing),
            _ => false
        };

        if (legal)
        {
            return;
        }

        var allowed = string.Join(", ", LegalActions(subscription));
        throw new InvalidSubscriptionOperationException(
            $"Cannot {action} subscription {subscription.Id} while it is {subscription.State}. Legal actions: {(allowed.Length == 0 ? "none" : allowed)}.",
            subscription.Id, subscription.State);
    }

    private static IEnumerable<SubscriptionLifecycleAction> LegalActions(BillingSubscription subscription)
    {
        if (subscription.State is BillingSubscriptionState.Active or BillingSubscriptionState.Trialing)
        {
            yield return SubscriptionLifecycleAction.Pause;
        }

        if (subscription.IsPaused)
        {
            yield return SubscriptionLifecycleAction.Resume;
        }

        if (!subscription.IsTerminated)
        {
            yield return SubscriptionLifecycleAction.Cancel;
        }

        if (!subscription.IsPaused
            && subscription.State is not (BillingSubscriptionState.Active or BillingSubscriptionState.Trialing))
        {
            yield return SubscriptionLifecycleAction.Reactivate;
        }
    }

    private async Task<BillingPlan> RequireSelectablePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plan = await _billingClient.FindPlanAsync(planHandle, cancellationToken);

        if (plan is null)
        {
            throw new InvalidBillingRequestException($"No plan with handle '{planHandle}' is available.");
        }

        if (plan.IsArchived)
        {
            throw new InvalidBillingRequestException($"Plan '{planHandle}' has been archived and can no longer be selected.");
        }

        return plan;
    }

    private async Task<BillingSubscription> RequireSubscriptionAsync(SubscriptionActor actor, int subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException(
                $"No subscription {subscriptionId} exists at the billing provider.", "ReadSubscription", 404);

        await EnsureAccessAsync(actor, subscription, cancellationToken);
        return subscription;
    }

    private async Task EnsureAccessAsync(SubscriptionActor actor, BillingSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (actor.IsAdministrator)
        {
            return;
        }

        if (string.Equals(subscription.CustomerReference, actor.UserReference, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The provider does not always echo the customer reference on a subscription; fall back to
        // comparing customer identifiers before refusing.
        if (subscription.CustomerId.HasValue)
        {
            var customer = await _billingClient.FindCustomerByReferenceAsync(actor.UserReference, cancellationToken);
            if (customer is not null && customer.Id == subscription.CustomerId.Value)
            {
                return;
            }
        }

        throw new SubscriptionAccessDeniedException(
            "This subscription does not belong to the signed in user.");
    }

    private async Task<BillingCustomer> CreateCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var separator = reference.IndexOf('@');
        if (separator <= 0)
        {
            throw new InvalidBillingRequestException(
                "A billing customer cannot be created without an email address for the signed in user.");
        }

        var givenName = reference[..separator];

        return await _billingClient.CreateCustomerAsync(reference, reference, givenName, CustomerSurname,
            cancellationToken);
    }

    private static bool PlanMatches(BillingSubscription subscription, string planHandle)
        => string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static string RequirePlanHandle(string planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new InvalidBillingRequestException("A plan handle is required.");
        }

        return planHandle.Trim();
    }

    private static string RequireUserReference(SubscriptionActor actor)
    {
        if (string.IsNullOrWhiteSpace(actor.UserReference))
        {
            throw new InvalidBillingRequestException("The signed in user could not be identified.");
        }

        return actor.UserReference;
    }

    /// <summary>
    /// Publishes best effort: eventing is in-process only and a failing handler must never undo a
    /// change the provider has already accepted (plan §2.5).
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
                "In-process publication of {NotificationType} failed after the billing change was applied: {Reason}",
                notification.GetType().Name, ex.Message);
        }
    }
}
