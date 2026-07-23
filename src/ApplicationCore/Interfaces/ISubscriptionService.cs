using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface. Orchestrates the billing client, enforces the rules that
/// belong to eShopOnWeb rather than the provider, and announces state changes in-process.
/// Every operation identifies the customer by their eShopOnWeb user reference (username/email).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrolls a user in a plan, creating their provider customer record if needed. Safe to
    /// repeat: an existing active subscription is returned rather than a second enrollment created.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 — the user's subscriptions, or an empty list if they have never subscribed.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage on the user's active subscription and reads back the running total.</summary>
    Task<UsageReport> RecordUsageAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage against a specific subscription, for admin/programmatic callers.</summary>
    Task<UsageReport> RecordUsageForSubscriptionAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — what changing plan would cost, shown before the customer confirms.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits a plan change. When <paramref name="previewedPaymentDue"/> is supplied it
    /// must still match a fresh preview, otherwise the commit is refused as stale.
    /// </summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        decimal? previewedPaymentDue, CancellationToken cancellationToken = default);

    /// <summary>UC4 — pauses an active subscription.</summary>
    Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — resumes a paused subscription.</summary>
    Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancels a subscription now or at the end of the current period.</summary>
    Task<CustomerSubscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivates a cancelled subscription.</summary>
    Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
