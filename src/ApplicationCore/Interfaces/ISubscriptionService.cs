using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case orchestration for the subscription module (mirrors <see cref="IOrderService"/>):
/// validates, drives the billing client, and publishes lifecycle notifications.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingSubscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <param name="ownerReference">The requesting user's reference for an ownership check, or null for an admin/any-subscription call.</param>
    Task<UsageResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, string? ownerReference, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, string? ownerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, PlanChangePreview previouslyShownPreview, string? ownerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, string? ownerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);
}
