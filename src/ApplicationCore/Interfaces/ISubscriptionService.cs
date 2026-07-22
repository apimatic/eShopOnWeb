using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (plan §3). Mirrors <see cref="IOrderService"/>: hosts orchestrate,
/// this service validates, drives the billing client and announces state changes in-process.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer may subscribe to.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the eShopOnWeb user in a plan. Idempotent on the user reference: a customer who
    /// already holds a live subscription gets that subscription back rather than a second enrolment.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(SubscriptionActor actor, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC1 — the subscriptions the provider holds for this user.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(SubscriptionActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one subscription the actor is allowed to see, or <c>null</c> when the provider has no
    /// such subscription.
    /// </summary>
    Task<BillingSubscription?> GetSubscriptionAsync(SubscriptionActor actor, int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage and reads back the running period-to-date total.</summary>
    Task<UsageReport> RecordUsageAsync(SubscriptionActor actor, int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC3 — quotes a plan change without committing anything.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(SubscriptionActor actor, int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 — commits a plan change. When <paramref name="previewedPaymentDue"/> is supplied it must
    /// still match a freshly taken preview, otherwise the commit is rejected as stale.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(SubscriptionActor actor, int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? previewedPaymentDue, CancellationToken cancellationToken = default);

    /// <summary>UC4 — pause, resume, cancel or reactivate a subscription.</summary>
    Task<SubscriptionLifecycleResult> ApplyLifecycleActionAsync(SubscriptionActor actor, int subscriptionId,
        SubscriptionLifecycleAction action, SubscriptionCancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken = default);
}
