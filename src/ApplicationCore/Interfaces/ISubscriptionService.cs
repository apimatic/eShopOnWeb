using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (mirrors <see cref="IOrderService"/>). Orchestrates the billing
/// client, enforces eShopOnWeb's own rules, and publishes the lifecycle notifications of §2.5.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the eShopOnWeb user in a plan, creating the provider-side customer if needed, and
    /// publishes <c>SubscriptionActivated</c>. Returns the existing active subscription instead of
    /// creating a second one when the user is already enrolled.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC1 step 7 / account page — the user's subscriptions; empty when they have none.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage against a subscription and reads back the period-to-date total.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records one unit of usage for the user's active subscription, if they have one.</summary>
    Task<UsageReport?> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — what the plan change would cost, before the customer commits.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits the plan change and publishes <c>SubscriptionPlanChanged</c>.
    /// When <paramref name="expectedNetAmount"/> is supplied it is re-checked against a fresh preview and
    /// the commit is rejected if the amount moved, so the customer is never charged an amount they were
    /// not shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        decimal? expectedNetAmount = null, CancellationToken cancellationToken = default);

    /// <summary>UC4 — pauses an active subscription and publishes <c>SubscriptionStateChanged</c>.</summary>
    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — resumes a paused subscription and publishes <c>SubscriptionStateChanged</c>.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancels now or at the period boundary and publishes <c>SubscriptionStateChanged</c>.</summary>
    Task<Subscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivates a cancelled subscription and publishes <c>SubscriptionStateChanged</c>.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
