using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription module's use-case surface (mirror <see cref="IOrderService"/>). Orchestrates
/// the billing client, enforces the eShopOnWeb-side rules the provider doesn't (ownership, legal
/// lifecycle transitions, stale-preview detection), and publishes MediatR notifications on
/// successful state changes (§2.5).
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1: enroll <paramref name="customerReference"/> in <paramref name="productHandle"/>. Idempotent — returns the existing active subscription if one already exists.</summary>
    Task<BillingSubscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1/UC4 read model: every subscription belonging to this eShopOnWeb user.</summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2: record usage against <paramref name="subscriptionId"/>. When <paramref name="actingAsAdmin"/> is false,
    /// the subscription must belong to <paramref name="customerReference"/>.
    /// </summary>
    Task<UsageRecordResult> RecordUsageAsync(string customerReference, bool actingAsAdmin, long subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 2: preview a plan change. Ownership rules mirror <see cref="RecordUsageAsync"/>.
    /// Requesting <see cref="PlanChangeTiming.AtNextRenewal"/> throws <see cref="Exceptions.PlanChangeNotSupportedException"/> —
    /// the Maxio SDK has no operation that defers a plan-change commit to the next renewal.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string customerReference, bool actingAsAdmin, long subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4: commit a previously-previewed plan change. <paramref name="expectedProratedAdjustmentInCents"/>
    /// must match a freshly recomputed preview or the commit is rejected (<see cref="Exceptions.StalePlanChangePreviewException"/>).
    /// </summary>
    Task<BillingSubscription> CommitPlanChangeAsync(string customerReference, bool actingAsAdmin, long subscriptionId, string targetProductHandle, PlanChangeTiming timing, long expectedProratedAdjustmentInCents, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(string customerReference, bool actingAsAdmin, long subscriptionId, CancellationToken cancellationToken = default);
}
