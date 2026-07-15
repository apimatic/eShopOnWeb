using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case orchestration for eShopOnWeb Subscribe (mirrors <see cref="IOrderService"/>): validates,
/// calls <see cref="IBillingClient"/>, and publishes the MediatR notification for each UC.
/// </summary>
/// <remarks>
/// Every operation that acts on an existing subscription takes an <c>ownerUserId</c>: when non-null
/// (a customer acting on "their own" subscription, plan.md UC2-UC4), the subscription's owning
/// customer reference must match or <see cref="Exceptions.SubscriptionNotFoundException"/> is thrown;
/// when null (an admin acting on "any" subscription), no ownership check is made.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1 steps 2-7. Idempotent: returns the existing subscription rather than enrolling a second time if one is already active/trialing.</summary>
    Task<Subscription> SubscribeAsync(string userId, string userEmail, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>The customer's subscriptions, for the "my subscriptions" view (UC1 success state).</summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>UC2 steps 1-4.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, string? ownerUserId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 steps 1-2.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string? ownerUserId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>UC3 steps 3-6. <paramref name="confirmedPreview"/> must be a preview freshly returned by <see cref="PreviewPlanChangeAsync"/>; a re-derived preview is compared against it and a mismatch throws <see cref="Exceptions.StalePlanChangePreviewException"/>.</summary>
    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string? ownerUserId, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default);

    /// <summary>UC4 pause.</summary>
    Task<Subscription> PauseAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>UC4 resume.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>UC4 cancel (immediate or end-of-period).</summary>
    Task<Subscription> CancelAsync(int subscriptionId, string? ownerUserId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>UC4 reactivate.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, string? ownerUserId, CancellationToken cancellationToken = default);
}
