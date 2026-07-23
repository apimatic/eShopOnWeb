using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1–UC4). Validates the request, drives the billing client,
/// and publishes the matching in-process notification.
/// </summary>
/// <remarks>
/// The <c>ownerReference</c> parameters scope an operation to one eShopOnWeb user: pass the signed-in
/// user's reference from customer-facing flows so a customer can only act on their own subscription,
/// or null from administrative flows that may act on any subscription.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrolls the eShopOnWeb user in a plan. Idempotent: an existing active subscription for
    /// this user is returned instead of creating a second enrollment.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 — the subscriptions eShopOnWeb holds for this user.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, scoped to <paramref name="ownerReference"/> when supplied.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records usage against the user's own active subscription.</summary>
    Task<UsageReport> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records usage against a specific subscription.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 — the running period-to-date usage for a subscription, or null when unavailable.</summary>
    Task<UsageReport?> GetUsageSummaryAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — the prorated cost of a plan change, before the customer commits.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string? ownerReference, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits a plan change. <paramref name="previewToken"/> is the
    /// <see cref="PlanChangePreview.Token"/> the customer was shown; a preview whose basis has moved
    /// since is rejected rather than applied at a different amount.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string? ownerReference, string targetPlanHandle, PlanChangeTiming timing, string previewToken, CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition, rejecting ones that are illegal from the current state.</summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, string? ownerReference, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason, CancellationToken cancellationToken = default);
}
