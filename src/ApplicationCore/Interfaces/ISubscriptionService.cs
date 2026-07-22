using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (plan.md UC1–UC4), mirroring <see cref="IOrderService"/>.
/// Callers pass the signed-in eShopOnWeb user's name; the service maps it onto the billing
/// provider's customer record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// UC1 step 1 — the plans a customer can subscribe to.
    /// </summary>
    Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the user in a plan. Idempotent: if the user already has a live subscription
    /// on that plan it is returned rather than a second one created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 step 7 / account page — the user's own subscriptions.
    /// </summary>
    Task<IReadOnlyCollection<Subscription>> GetMySubscriptionsAsync(string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records usage against the user's own live subscription.
    /// </summary>
    Task<UsageReport> RecordUsageAsync(string userName, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 (admin) — records usage against any subscription by id.
    /// </summary>
    Task<UsageReport> RecordUsageForSubscriptionAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 step 3 — the units accrued so far this period, or <c>null</c> when unavailable.
    /// </summary>
    Task<decimal?> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 2 — quotes what a plan change would cost, without committing it.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits a previously previewed plan change. The commit is refused if the
    /// provider's quote has moved since <paramref name="confirmedPreview"/> was shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC4 — applies a lifecycle transition, rejecting it locally when it is illegal from the
    /// subscription's current state.
    /// </summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action,
        bool cancelAtEndOfPeriod = false, string? reason = null, CancellationToken cancellationToken = default);
}
