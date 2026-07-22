using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1–UC4), mirroring <see cref="IOrderService"/>.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can subscribe to.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrolls the eShopOnWeb user in a plan, creating the provider-side customer if needed.
    /// Idempotent: an already-active subscription for the user is returned rather than duplicated.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string? planHandle = null, CancellationToken cancellationToken = default);

    /// <summary>UC1 — the subscriptions currently held by the eShopOnWeb user.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — reports pay-as-you-go usage against a subscription and reads back the running total.
    /// Pass a <paramref name="userReference"/> to restrict the action to that user's own
    /// subscription, or null for the admin surface.
    /// </summary>
    Task<UsageReceipt> RecordUsageAsync(string? userReference, int subscriptionId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default);

    /// <summary>UC3 — the prorated cost of a plan change, shown before the customer commits.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string? userReference, int subscriptionId, string targetPlanHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 — commits a plan change. When <paramref name="confirmedPaymentDue"/> is supplied it must
    /// still match a freshly computed preview, otherwise the commit is rejected as stale.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(string? userReference, int subscriptionId, string targetPlanHandle, bool applyImmediately, decimal? confirmedPaymentDue = null, CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition, rejecting illegal ones before any provider call.</summary>
    Task<SubscriptionLifecycleResult> ApplyLifecycleActionAsync(string? userReference, int subscriptionId, SubscriptionLifecycleAction action, bool endOfPeriod = false, string? reason = null, CancellationToken cancellationToken = default);
}
