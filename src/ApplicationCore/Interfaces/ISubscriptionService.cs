using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case orchestration for eShopOnWeb Subscribe (mirrors <see cref="IOrderService"/>): validates input,
/// enforces ownership and lifecycle-state rules, calls the billing provider through <see cref="IBillingClient"/>,
/// and publishes the corresponding MediatR notification on every state change.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>UC1: enroll the eShopOnWeb user (identified by <paramref name="customerReference"/>) in a plan.</summary>
    Task<BillingSubscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string planHandle, CancellationToken ct = default);

    /// <summary>The subscriptions belonging to the given eShopOnWeb user, or an empty list if none exist yet.</summary>
    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct = default);

    /// <summary>UC2: record pay-as-you-go usage. <paramref name="isAdmin"/> allows acting on any subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(string customerReference, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC3 step 1-2: preview a plan change and receive back a signed, time-limited preview token.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string customerReference, int subscriptionId, string targetPlanHandle, bool applyNow, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC3 step 3-4: commit a previously previewed plan change. Rejects a stale/tampered token.</summary>
    Task<BillingSubscription> CommitPlanChangeAsync(string customerReference, string previewToken, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4: pause.</summary>
    Task<BillingSubscription> PauseAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4: resume a paused subscription.</summary>
    Task<BillingSubscription> ResumeAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4: cancel, either immediately or at the end of the current billing period.</summary>
    Task<BillingSubscription> CancelAsync(string customerReference, int subscriptionId, bool endOfPeriod, string? reason, bool isAdmin, CancellationToken ct = default);

    /// <summary>UC4: reactivate a canceled/expired subscription.</summary>
    Task<BillingSubscription> ReactivateAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default);
}
