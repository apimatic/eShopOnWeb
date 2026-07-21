using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Use-case surface for the subscription feature (mirrors <see cref="IOrderService"/>).</summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1 — enrolls the user in the given plan, or returns their existing active subscription if already enrolled.</summary>
    Task<Subscription> SubscribeAsync(string customerReference, string firstName, string lastName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records one usage report against the subscription's metered component.</summary>
    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 1-2 — previews the cost of moving to <paramref name="targetPlanHandle"/>.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 3-4 — commits a previously previewed plan change; rejects if the preview is stale.</summary>
    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, decimal expectedProratedAmount, CancellationToken cancellationToken = default);

    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
