using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The subscription use-case surface consumed by the Web storefront and PublicApi (mirrors IOrderService).</summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1: enrolls <paramref name="buyerId"/> in <paramref name="productHandle"/>, or returns their existing active subscription.</summary>
    Task<Subscription> SubscribeAsync(string buyerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>The signed-in user's own subscription, or null if they have none.</summary>
    Task<Subscription?> GetMySubscriptionAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>UC2: records usage against <paramref name="subscriptionId"/>'s metered component, then reads back the period-to-date total.</summary>
    Task<(UsageRecord Usage, UsagePeriodSummary Summary)> RecordUsageAsync(string actingBuyerId, bool isAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    Task<UsagePeriodSummary> GetUsageSummaryAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC2 automatic hook: one order placed records one api-call unit against the buyer's active subscription, if any. Best-effort - never throws.</summary>
    Task RecordOrderPlacedUsageAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2: previews the prorated cost/credit of a plan change without applying it.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 4: commits a previously previewed plan change. Rejected if the preview has gone stale.</summary>
    Task<Subscription> CommitPlanChangeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, string targetProductHandle, bool immediate, string commitToken, CancellationToken cancellationToken = default);

    Task<Subscription> PauseAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelAsync(string actingBuyerId, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);
}
