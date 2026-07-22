using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface, mirroring <see cref="IOrderService"/>: hosts orchestrate and
/// present, this service validates, drives the billing provider through <see cref="IBillingClient"/>
/// and announces state changes in-process.
/// <para>
/// Members taking a <c>userReference</c> are customer-scoped and enforce ownership; the
/// <c>*ForSubscriptionAsync</c> members are the administrator equivalents that act on any
/// subscription and are only reachable from an administrator-guarded surface.
/// </para>
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols the given eShopOnWeb user in a plan (UC1). Ensures the provider-side customer exists
    /// first, and returns the customer's existing subscription instead of enrolling twice when they
    /// are already subscribed to that plan.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the given user's subscriptions. Returns empty when they have no provider customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Reads one of the given user's subscriptions, enforcing ownership.</summary>
    Task<CustomerSubscription> GetMySubscriptionAsync(string userReference, int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage on the given user's live subscription (UC2).</summary>
    Task<UsageSummary> RecordUsageAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage on any subscription. Administrator surface (UC2).</summary>
    Task<UsageSummary> RecordUsageForSubscriptionAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Prices a plan change for the given user's subscription without committing it (UC3).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previously previewed plan change (UC3). <paramref name="previewSignature"/> is the
    /// <see cref="PlanChangePreview.Signature"/> the customer confirmed; the change is refused if a
    /// fresh preview no longer matches it.
    /// </summary>
    Task<CustomerSubscription> ChangePlanAsync(string userReference, int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, string previewSignature, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to the given user's subscription (UC4).</summary>
    Task<CustomerSubscription> ApplyLifecycleActionAsync(string userReference, int subscriptionId, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming = CancellationTiming.Immediate, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to any subscription. Administrator surface (UC4).</summary>
    Task<CustomerSubscription> ApplyLifecycleActionForSubscriptionAsync(int subscriptionId, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming = CancellationTiming.Immediate, string? reason = null, CancellationToken cancellationToken = default);
}
