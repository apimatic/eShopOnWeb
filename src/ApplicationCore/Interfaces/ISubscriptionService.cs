using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case orchestration for the subscription module (mirrors <see cref="IOrderService"/>):
/// validates, calls <see cref="IBillingClient"/>, and publishes MediatR notifications on
/// successful state changes (§2.5). Owns the "own subscription vs. admin" access check for every
/// member that takes a <paramref name="requestingBuyerId"/> and identifies a subscription by id.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Enrolls <paramref name="buyerId"/> in <paramref name="productHandle"/>, or returns their existing active subscription if one already exists (UC1 dedupe).</summary>
    Task<Subscription> SubscribeAsync(string buyerId, string email, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<UsageSummary> RecordUsageAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Best-effort automatic usage hook for "one order placed → one billable unit" (§UC2). Never
    /// throws: if the buyer has no active subscription, or the provider call fails, it logs and
    /// returns without affecting the caller's order flow.
    /// </summary>
    Task RecordUsageForOrderAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Re-validates and re-previews before committing; throws <see cref="Exceptions.StalePlanChangePreviewException"/> if <paramref name="previewedAmountInCents"/> no longer matches (§UC3).</summary>
    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, string targetProductHandle, PlanChangeTiming timing, long previewedAmountInCents, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, string requestingBuyerId, bool isAdmin, CancellationToken cancellationToken = default);
}
