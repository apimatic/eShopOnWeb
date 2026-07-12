using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (mirrors <see cref="IOrderService"/>): validates, calls the
/// billing client, and publishes the MediatR notification for each UC. Consumed by both the Web
/// storefront (customer flows) and PublicApi (customer + admin flows).
/// </summary>
/// <remarks>
/// Actions that can be performed either by their owning customer or by an admin (UC2/UC3/UC4) take
/// an <c>ownerReference</c> parameter: pass the caller's stable user reference to enforce that the
/// subscription belongs to them, or <c>null</c> to skip the ownership check for an admin caller.
/// </remarks>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    Task<Subscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);

    Task<Subscription> GetSubscriptionAsync(string? ownerReference, int subscriptionId, CancellationToken ct = default);

    Task<UsageRecord> RecordUsageAsync(string? ownerReference, int subscriptionId, double quantity, string? memo, CancellationToken ct = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(string? ownerReference, int subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken ct = default);

    /// <summary>
    /// Commits a plan change. <paramref name="expectedProratedAdjustmentInCents"/> and
    /// <paramref name="expectedChargeInCents"/> must match the amounts most recently previewed;
    /// a fresh preview is taken and compared before committing, and
    /// <see cref="Exceptions.StalePlanChangePreviewException"/> is thrown if they no longer match
    /// (UC3: never silently apply a different amount than the one shown).
    /// </summary>
    Task<Subscription> CommitPlanChangeAsync(
        string? ownerReference,
        int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        int expectedProratedAdjustmentInCents,
        int expectedChargeInCents,
        CancellationToken ct = default);

    Task<Subscription> ApplyLifecycleActionAsync(string? ownerReference, int subscriptionId, SubscriptionLifecycleAction action, string? reason, CancellationToken ct = default);

    /// <summary>
    /// The UC2 "one order placed → one billable unit" hook. Best-effort: if the user has no active
    /// subscription, or the provider call fails, this returns without throwing so a Maxio failure
    /// never blocks eShopOnWeb's order lifecycle.
    /// </summary>
    Task RecordOrderPlacedUsageAsync(string userReference, CancellationToken ct = default);
}
