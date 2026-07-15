using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case surface for the subscription module (mirrors <see cref="IOrderService"/>). Orchestrates the
/// billing client, enforces the domain's lifecycle rules, and publishes MediatR notifications on
/// meaningful state changes (plan.md §2.5).
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer can subscribe to (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the eShopOnWeb user in the given plan (UC1). Idempotent: if the user already has an active
    /// subscription on any plan in the configured product family, that subscription is returned instead of
    /// creating a duplicate.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to the given eShopOnWeb user.</summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records usage against a subscription's metered component (UC2). <paramref name="actingAsAdmin"/>
    /// bypasses the ownership check so an administrator can report usage for any subscription.
    /// </summary>
    Task<BillingUsageReading> RecordUsageAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Previews a plan change's proration/timing impact before it is committed (UC3).</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change previously shown via <see cref="PreviewPlanChangeAsync"/>. Rejects the commit
    /// (<see cref="Exceptions.PlanChangePreviewStaleException"/>) if <paramref name="stalenessToken"/> no
    /// longer matches the subscription's current state.
    /// </summary>
    Task<Subscription> CommitPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, string stalenessToken, CancellationToken cancellationToken = default);

    /// <summary>Pauses a subscription (UC4). Legal only while the subscription is active.</summary>
    Task<Subscription> PauseAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused subscription (UC4).</summary>
    Task<Subscription> ResumeAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately or at the end of the current period (UC4).</summary>
    Task<Subscription> CancelAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription (UC4).</summary>
    Task<Subscription> ReactivateAsync(string actingUserId, bool actingAsAdmin, int subscriptionId, CancellationToken cancellationToken = default);
}
