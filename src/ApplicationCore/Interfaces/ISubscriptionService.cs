using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1–UC4). Mirrors <see cref="IOrderService"/>: it
/// orchestrates the billing client, enforces the domain rules, and announces state changes
/// in-process through MediatR.
/// </summary>
/// <remarks>
/// Lifecycle and usage operations take an <c>ownerBuyerId</c>. When it is supplied the caller is
/// acting on their own subscription and ownership is enforced; when it is <c>null</c> the caller
/// is an administrator acting on any subscription.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the eShopOnWeb user in a plan. Idempotent: if the user already holds an
    /// active subscription it is returned rather than a second one being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string buyerId, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC1 step 7 / "my subscriptions" — every subscription held by the user.</summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>The user's current billable subscription, or <c>null</c> when they have none.</summary>
    Task<Subscription?> GetActiveSubscriptionForUserAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered consumption and reads back the period-to-date total.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerBuyerId, decimal quantity,
        string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records metered consumption against the user's own active subscription. Returns
    /// <c>null</c> when the user has no active subscription, so callers driven by an
    /// eShopOnWeb event (an order being placed) can ignore non-subscribers.
    /// </summary>
    Task<UsageReport?> RecordUsageForUserAsync(string buyerId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — the units accrued so far this period, without recording anything.</summary>
    Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — quotes the cost of a plan change before the customer confirms.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string? ownerBuyerId,
        string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 steps 3–6 — commits a plan change. When <paramref name="confirmedPreview"/> is
    /// supplied it is re-validated against a fresh preview and the commit is refused if the
    /// amounts have moved.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string? ownerBuyerId, string targetPlanHandle,
        PlanChangeTiming timing, PlanChangePreview? confirmedPreview,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — puts a subscription on hold.</summary>
    Task<Subscription> PauseAsync(int subscriptionId, string? ownerBuyerId,
        DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default);

    /// <summary>UC4 — takes a subscription off hold.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancels a subscription, immediately or at the end of the period.</summary>
    Task<Subscription> CancelAsync(int subscriptionId, string? ownerBuyerId, CancellationTiming timing,
        string? reason, CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivates a cancelled or expired subscription.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, string? ownerBuyerId,
        CancellationToken cancellationToken = default);
}
