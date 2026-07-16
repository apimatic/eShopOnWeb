using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case surface for eShopOnWeb Subscribe (mirrors <see cref="IOrderService"/>). Orchestrates the
/// billing client, ownership checks, and best-effort MediatR notifications for UC1-UC4.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>UC1: enroll (or return the existing active enrollment for) the given eShopOnWeb user.</summary>
    Task<Subscription> SubscribeAsync(string userId, string userEmail, string productHandle, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken ct = default);

    Task<Subscription> GetSubscriptionAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC2: bill for actor-reported consumption of the metered component.</summary>
    Task<UsageRecordResult> RecordUsageAsync(string userId, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC2 automatic hook: one eShopOnWeb order placed -> one usage unit; a no-op if the buyer has no active subscription.</summary>
    Task RecordAutomaticUsageAsync(string userId, int quantity, string memo, CancellationToken ct = default);

    /// <summary>UC3: preview the prorated cost/credit of moving to <paramref name="targetProductHandle"/>.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC3: commit a previously-previewed plan change; rejects if the amounts have gone stale.</summary>
    Task<Subscription> CommitPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, PlanChangePreview expectedPreview, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4.</summary>
    Task<Subscription> PauseAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4.</summary>
    Task<Subscription> ResumeAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4.</summary>
    Task<Subscription> CancelAsync(string userId, int subscriptionId, bool cancelAtEndOfPeriod, string? reason, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4.</summary>
    Task<Subscription> ReactivateAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default);
}
