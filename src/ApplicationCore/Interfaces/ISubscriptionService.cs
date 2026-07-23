using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface: everything the storefront and the public API can ask for.
/// Mirrors <see cref="IOrderService"/> — orchestration lives here, provider I/O lives behind
/// <see cref="IBillingClient"/>, and lifecycle facts are announced through in-process notifications.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer can subscribe to.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an eShopOnWeb user in a plan and publishes <c>SubscriptionActivated</c>.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly: when the user already holds an active subscription, that existing
    /// subscription is returned rather than a second enrollment being created.
    /// </remarks>
    Task<BillingSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by a user, in any state.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's current active subscription, or null when they hold none.
    /// </summary>
    Task<BillingSubscription?> GetActiveSubscriptionAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against a specific subscription. Used by admin and programmatic callers.
    /// </summary>
    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against the user's own active subscription.
    /// </summary>
    Task<UsageRecordResult> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date metered usage for a subscription.
    /// </summary>
    Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quotes what moving a subscription to another plan would cost. Nothing is committed.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previously previewed plan change and publishes <c>SubscriptionPlanChanged</c>.
    /// </summary>
    /// <param name="previewFingerprint">
    /// The <see cref="PlanChangePreview.Fingerprint"/> the customer confirmed. The quote is
    /// recomputed and the change is refused with
    /// <see cref="Exceptions.StalePlanChangePreviewException"/> if the basis has moved, so the
    /// amount charged can never differ from the amount shown.
    /// </param>
    Task<BillingSubscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a lifecycle transition and publishes <c>SubscriptionStateChanged</c>.
    /// Illegal transitions are rejected before any provider call is made.
    /// </summary>
    Task<BillingSubscription> ApplyLifecycleActionAsync(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming = CancellationTiming.Immediate,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
