using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case surface for eShopOnWeb Subscribe (mirrors <see cref="IOrderService"/>): validates
/// requests, drives the matching <see cref="IBillingClient"/> operation, and publishes the
/// corresponding in-process MediatR notification on success.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="userReference"/> in <paramref name="planHandle"/> (UC1). Idempotent:
    /// if the customer already has an active or trialing subscription, that subscription is
    /// returned instead of creating a second one.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a specific subscription, verifying it belongs to <paramref name="userReference"/>
    /// unless <paramref name="isAdmin"/> is set. Throws <see cref="Exceptions.SubscriptionValidationException"/>
    /// when the subscription does not belong to the caller.
    /// </summary>
    Task<BillingSubscription> GetSubscriptionForUserAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>Records usage against the metered component (UC2). Rejects zero/negative quantities before any provider call.</summary>
    Task<BillingUsageRecordResult> RecordUsageAsync(string userReference, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken cancellationToken = default);

    Task<int> GetUsageBalanceAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews a plan change (UC3). When <paramref name="applyNow"/> is true this previews the
    /// prorated charge/credit from the provider; otherwise it composes the at-renewal price (no
    /// proration) from already-known plan/subscription data, since the provider has no delayed-change
    /// preview operation.
    /// </summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previously previewed plan change. <paramref name="expectedProratedAdjustmentInCents"/>
    /// must match a freshly recomputed preview or the commit is rejected as stale (§6, Phase 4).
    /// </summary>
    Task<BillingSubscription> CommitPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, bool applyNow, int? expectedProratedAdjustmentInCents, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelAsync(string userReference, int subscriptionId, bool endOfPeriod, bool isAdmin, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);
}
