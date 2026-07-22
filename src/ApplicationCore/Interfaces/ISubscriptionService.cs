using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface, consumed by the storefront and the PublicApi alike.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer may subscribe to.</summary>
    Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — ensures a provider customer exists for this eShopOnWeb user and enrols them in
    /// <paramref name="planHandle"/> (the configured default plan when null). Repeated calls return the
    /// existing active subscription instead of enrolling twice.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(string userReference, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 — the subscriptions belonging to this eShopOnWeb user. Empty when there are none.</summary>
    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage on the user's subscription and reads back the running total.</summary>
    Task<UsageReport> RecordUsageAsync(string userReference, int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 — the prorated cost of moving to <paramref name="targetPlanHandle"/>, before committing.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 — commits a plan change. When <paramref name="acknowledgedProratedAdjustment"/> is supplied it
    /// is re-checked against a fresh preview and the commit is rejected if the amount has moved.
    /// </summary>
    Task<BillingSubscription> ChangePlanAsync(string userReference, int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? acknowledgedProratedAdjustment, CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition to the user's subscription.</summary>
    Task<BillingSubscription> ApplyLifecycleActionAsync(string userReference, int subscriptionId,
        SubscriptionLifecycleAction action, string? reason, CancellationToken cancellationToken = default);
}
