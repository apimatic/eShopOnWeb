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
/// Orchestrates the subscription use cases (plan.md UC1–UC4): validate, call the billing client,
/// publish the in-process notification. Mirrors <see cref="OrderService"/>.
/// </summary>
/// <remarks>
/// The mapping between an eShopOnWeb user and their billing records is stateless (plan.md §8): it
/// is re-derived on every call from the user's reference, which the provider stores on the customer
/// record. That makes "ensure a customer exists" naturally idempotent and means there is no local
/// state to drift out of sync.
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

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsForUserAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            // The user has never subscribed. That is not an error — they simply have nothing yet.
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer, cancellationToken);
    }

    public async Task<Subscription> SubscribeAsync(string userReference,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        // Resolving against the configured family's plan list is also the authorization check:
        // a caller cannot enrol themselves in an arbitrary product elsewhere on the site.
        var plan = await ResolvePlanAsync(productHandle, cancellationToken);

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken)
                       ?? await CreateCustomerAsync(userReference, cancellationToken);

        // UC1: a duplicate subscribe (double-click, repeated call) must return the existing
        // enrolment rather than creating a second one.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive);
        if (alreadyActive is not null)
        {
            _logger.LogInformation(
                "{0} is already subscribed (subscription {1}, plan {2}); returning the existing subscription.",
                userReference,
                alreadyActive.Id,
                alreadyActive.Plan.Handle);
            return alreadyActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer, plan, cancellationToken);

        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId,
        int quantity,
        string? memo,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        // Rejected before any provider call (UC2 failure scenarios).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetSubscriptionAsync(subscriptionId, actingUserReference, cancellationToken);

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<IReadOnlyList<UsageReport>> RecordUsageForUserAsync(string userReference,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscriptions = await ListSubscriptionsForUserAsync(userReference, cancellationToken);
        var active = subscriptions.Where(s => s.CanRecordUsage).ToArray();
        if (active.Length == 0)
        {
            return Array.Empty<UsageReport>();
        }

        var reports = new List<UsageReport>(active.Length);
        foreach (var subscription in active)
        {
            reports.Add(await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken));
        }

        return reports;
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetSubscriptionAsync(subscriptionId, actingUserReference, cancellationToken);
        var targetPlan = await ResolveTargetPlanAsync(subscription, targetProductHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription, targetPlan, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        string confirmedFingerprint,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.NullOrWhiteSpace(confirmedFingerprint, nameof(confirmedFingerprint));

        var subscription = await GetSubscriptionAsync(subscriptionId, actingUserReference, cancellationToken);
        var targetPlan = await ResolveTargetPlanAsync(subscription, targetProductHandle, cancellationToken);

        // Re-price immediately before committing. If the customer would now be charged something
        // other than what they were shown, refuse and make them look again (UC3, §6 Phase 4).
        var currentPreview = await _billingClient.PreviewPlanChangeAsync(
            subscription, targetPlan, timing, cancellationToken);

        if (!string.Equals(currentPreview.Fingerprint, confirmedFingerprint, StringComparison.Ordinal))
        {
            throw new StalePlanChangePreviewException(subscriptionId);
        }

        var previousPlan = subscription.Plan;
        var updated = await _billingClient.ChangePlanAsync(subscription, targetPlan, timing, cancellationToken);

        await PublishAsync(
            new SubscriptionPlanChanged(updated, previousPlan, targetPlan, timing, currentPreview.NetAmount),
            cancellationToken);

        return updated;
    }

    public Task<Subscription> PauseAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        return ApplyLifecycleActionAsync(subscriptionId,
            actingUserReference,
            action: "pause",
            isLegal: s => s.CanPause,
            apply: (id, ct) => _billingClient.PauseSubscriptionAsync(id, ct),
            cancellationToken);
    }

    public Task<Subscription> ResumeAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        return ApplyLifecycleActionAsync(subscriptionId,
            actingUserReference,
            action: "resume",
            isLegal: s => s.CanResume,
            apply: (id, ct) => _billingClient.ResumeSubscriptionAsync(id, ct),
            cancellationToken);
    }

    public Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        return ApplyLifecycleActionAsync(subscriptionId,
            actingUserReference,
            action: timing == CancellationTiming.EndOfPeriod ? "cancel at the end of the period" : "cancel",
            isLegal: s => s.CanCancel,
            apply: (id, ct) => _billingClient.CancelSubscriptionAsync(id, timing, reason, ct),
            cancellationToken);
    }

    public Task<Subscription> ReactivateAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        return ApplyLifecycleActionAsync(subscriptionId,
            actingUserReference,
            action: "reactivate",
            isLegal: s => s.CanReactivate,
            apply: (id, ct) => _billingClient.ReactivateSubscriptionAsync(id, ct),
            cancellationToken);
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var subscription = await _billingClient.FindSubscriptionByIdAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        // A null acting reference means an administrator surface, which may act on any subscription.
        // Otherwise the subscription must belong to the caller — reported as "not found" so that
        // subscription ids belonging to other users cannot be probed.
        if (actingUserReference is not null &&
            !string.Equals(subscription.UserReference, actingUserReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private async Task<UsageReport> RecordUsageCoreAsync(Subscription subscription,
        int quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        if (!subscription.CanRecordUsage)
        {
            throw new InvalidSubscriptionTransitionException(
                subscription.Id,
                "record usage against",
                subscription.State,
                LegalActionsFor(subscription));
        }

        // Refuses to record when the configured component is missing or of the wrong kind.
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);

        var recorded = await _billingClient.RecordUsageAsync(
            subscription.Id, component, quantity, memo, cancellationToken);

        try
        {
            var periodToDate = await _billingClient.GetPeriodToDateUsageAsync(
                subscription, component, cancellationToken);

            return UsageReport.WithTotal(recorded, periodToDate, component.UnitPrice);
        }
        catch (BillingProviderException ex)
        {
            // The usage is already recorded. Losing the running total must not turn a successful
            // write into a failure, and must never trigger a resend (UC2 failure scenarios).
            _logger.LogWarning(
                "Usage {0} was recorded against subscription {1} but the period-to-date total could not be read: {2}",
                recorded.Id,
                subscription.Id,
                ex.Message);

            return UsageReport.WithoutTotal(recorded, "The running total is temporarily unavailable.");
        }
    }

    private async Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId,
        string? actingUserReference,
        string action,
        Func<Subscription, bool> isLegal,
        Func<int, CancellationToken, Task<Subscription>> apply,
        CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionAsync(subscriptionId, actingUserReference, cancellationToken);

        if (!isLegal(subscription))
        {
            // Rejected locally: no provider call is made (UC4 failure scenarios).
            throw new InvalidSubscriptionTransitionException(
                subscriptionId, action, subscription.State, LegalActionsFor(subscription));
        }

        var previousState = subscription.State;
        var updated = await apply(subscriptionId, cancellationToken);

        await PublishAsync(new SubscriptionStateChanged(updated, previousState, action), cancellationToken);

        return updated;
    }

    private async Task<BillingPlan> ResolvePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(
            p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"No available plan with handle '{productHandle}' was found in the configured product family.");
        }

        return plan;
    }

    private async Task<BillingPlan> ResolveTargetPlanAsync(Subscription subscription,
        string targetProductHandle,
        CancellationToken cancellationToken)
    {
        if (!subscription.CanChangePlan)
        {
            throw new InvalidSubscriptionTransitionException(
                subscription.Id, "change the plan of", subscription.State, LegalActionsFor(subscription));
        }

        if (string.Equals(subscription.Plan.Handle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            // A no-op change is rejected before any provider call (UC3 failure scenarios).
            throw new InvalidSubscriptionTransitionException(
                subscription.Id,
                "change plan",
                subscription.State,
                LegalActionsFor(subscription),
                $"This subscription is already on {subscription.Plan.Name}. Choose a different plan to change to.");
        }

        return await ResolvePlanAsync(targetProductHandle, cancellationToken);
    }

    private async Task<BillingCustomer> CreateCustomerAsync(string userReference, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(userReference);
        return await _billingClient.CreateCustomerAsync(
            userReference, userReference, firstName, lastName, cancellationToken);
    }

    /// <summary>
    /// eShopOnWeb identifies users by their email/username (plan.md §4.4) and holds no separate
    /// given/family name, while the provider requires both. Derive something readable from the
    /// local part rather than sending empty strings.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : userReference;
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static IEnumerable<string> LegalActionsFor(Subscription subscription)
    {
        if (subscription.CanPause) yield return "pause";
        if (subscription.CanResume) yield return "resume";
        if (subscription.CanCancel) yield return "cancel";
        if (subscription.CanReactivate) yield return "reactivate";
        if (subscription.CanChangePlan) yield return "change plan";
        if (subscription.CanRecordUsage) yield return "record usage";
    }

    /// <summary>
    /// Eventing is in-process and best-effort (plan.md §2.5): the provider call has already
    /// succeeded, so a failing handler is logged and never rolls the change back.
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
                "The {0} notification could not be delivered in-process: {1}",
                notification.GetType().Name,
                ex.Message);
        }
    }
}
