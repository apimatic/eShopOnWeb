using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (§4.2), mirroring <see cref="IOrderService"/>: it validates
/// the request, drives the billing client, and publishes the matching in-process notification.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer can subscribe to (UC1 step 1).</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an eShopOnWeb user in a plan (UC1). Ensures the provider-side customer exists first,
    /// and returns the user's existing active subscription instead of enrolling twice when one is
    /// already active — so a double-click never creates a second enrollment.
    /// </summary>
    Task<Subscription> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every subscription belonging to an eShopOnWeb user, or an empty collection when the
    /// user has never subscribed.
    /// </summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns one of the user's subscriptions, or <c>null</c> when it is not theirs or does not exist.</summary>
    Task<Subscription?> GetSubscriptionForUserAsync(string buyerId, long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against the user's active subscription and reads back the running
    /// period-to-date total (UC2).
    /// </summary>
    Task<UsageRecordResult> RecordUsageAsync(string buyerId, int quantity, string? memo = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against a specific subscription. Used by the admin surface, which may
    /// act on any subscription rather than only the caller's own (UC2 actor: Admin).
    /// </summary>
    Task<UsageRecordResult> RecordUsageForSubscriptionAsync(long subscriptionId, int quantity, string? memo = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the period-to-date usage total without recording anything (UC2's usage panel).</summary>
    Task<UsageRecordResult> GetUsageSummaryAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Previews the cost of moving a subscription to another plan (UC3 step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string buyerId, long subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previewed plan change (UC3 step 4). <paramref name="confirmedPreviewFingerprint"/>
    /// is the <see cref="PlanChangePreview.Fingerprint"/> the customer confirmed; the commit is
    /// rejected if a fresh preview no longer matches it, so the amount charged is never different
    /// from the amount shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(string buyerId, long subscriptionId, string targetPlanHandle, PlanChangeTiming timing, string confirmedPreviewFingerprint, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to a subscription (UC4).</summary>
    Task<Subscription> ApplyLifecycleActionAsync(string buyerId, long subscriptionId, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming = CancellationTiming.Immediate, string? reason = null, CancellationToken cancellationToken = default);
}
