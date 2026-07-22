using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1-UC4). Orchestrates the billing seam, enforces the domain
/// rules that must hold before a provider call, and announces lifecycle facts in-process.
/// </summary>
/// <remarks>
/// Members that take a <c>userName</c> are the customer-scoped flows: they verify the subscription
/// belongs to that eShopOnWeb user before acting on it. The <c>...ForSubscriptionAsync</c> members are
/// the operator/admin flows that act on any subscription and must therefore only be reachable from an
/// administrator-guarded surface.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrolls the eShopOnWeb user in a plan, creating the provider-side customer if needed.
    /// Idempotent: if the user already holds a live subscription on that plan it is returned unchanged
    /// rather than enrolling twice.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 — every subscription belonging to this eShopOnWeb user.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>The user's currently billable subscription, or null when they hold none.</summary>
    Task<CustomerSubscription?> GetActiveSubscriptionAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 precondition — the configured metered component, verified to be of metered kind.
    /// </summary>
    Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records usage against the user's own active subscription and reads back the running
    /// period-to-date total.
    /// </summary>
    Task<UsageSummary> RecordUsageAsync(string userName, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 (admin) — records usage against any subscription.</summary>
    Task<UsageSummary> RecordUsageForSubscriptionAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 — the running period-to-date billable unit balance for one of the user's subscriptions.</summary>
    Task<int?> GetPeriodToDateUsageAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — the prorated cost (or next-period price) of moving to another plan.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userName,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits the plan change. <paramref name="confirmedFingerprint"/> is the
    /// <see cref="PlanChangePreview.Fingerprint"/> the customer confirmed; the service re-previews and
    /// refuses the commit if the basis moved, so the amount applied is always the amount shown.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(string userName,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string? confirmedFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition to one of the user's own subscriptions.</summary>
    Task<CustomerSubscription> ApplyLifecycleActionAsync(string userName,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 (admin) — applies a lifecycle transition to any subscription.</summary>
    Task<CustomerSubscription> ApplyLifecycleActionForSubscriptionAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default);
}
