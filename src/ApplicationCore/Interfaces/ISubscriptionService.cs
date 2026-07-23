using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface, mirroring <see cref="IOrderService"/>: it validates the request,
/// drives <see cref="IBillingClient"/>, and announces the outcome through MediatR (plan.md §4.2).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1, step 1 — the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the eShopOnWeb user in a plan. Idempotent: if the user already holds a live
    /// subscription it is returned unchanged rather than a second enrolment being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(
        string userName,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC1, step 7 / UC4 — the subscriptions belonging to an eShopOnWeb user.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription the actor is allowed to see.</summary>
    Task<Subscription> GetSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records pay-as-you-go usage against a specific subscription.</summary>
    Task<UsageReport> RecordUsageAsync(
        SubscriptionActor actor,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records usage against the user's own active subscription. Returns <c>null</c> when the user
    /// has no active subscription, which is the normal case for the automatic order-placed hook.
    /// </summary>
    Task<UsageReport?> RecordUsageForUserAsync(
        string userName,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — the running period-to-date usage for a subscription, or <c>null</c> if unavailable.</summary>
    Task<UsageReport> GetUsageSummaryAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>UC3, step 2 — the prorated cost of moving to another plan.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC3, step 4 — commits a plan change with the confirmed timing and amount.</summary>
    Task<Subscription> ChangePlanAsync(
        SubscriptionActor actor,
        int subscriptionId,
        PlanChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition to a subscription.</summary>
    Task<Subscription> ExecuteLifecycleActionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        SubscriptionLifecycleRequest request,
        CancellationToken cancellationToken = default);
}
