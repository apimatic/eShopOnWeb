using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// Use-case orchestration for the subscription module (mirrors IOrderService): validates,
// calls IBillingClient, and publishes the corresponding MediatR notification.
//
// Every member that targets an existing subscription takes an "ownerReference": pass the
// caller's stable eShopOnWeb identity (User.Identity.Name) to enforce that the subscription
// belongs to that caller, or null for an admin/programmatic caller acting on any subscription.
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default);

    Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle,
        CancellationToken ct = default);

    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string customerReference,
        CancellationToken ct = default);

    Task<CustomerSubscription> GetSubscriptionAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default);

    Task<UsageRecordResult> RecordUsageAsync(string? ownerReference, int subscriptionId, double quantity,
        string? memo, CancellationToken ct = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(string? ownerReference, int subscriptionId,
        string targetPlanHandle, CancellationToken ct = default);

    Task<CustomerSubscription> CommitPlanChangeAsync(string? ownerReference, int subscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, long? expectedProratedAdjustmentInCents,
        CancellationToken ct = default);

    Task<CustomerSubscription> PauseAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default);

    Task<CustomerSubscription> ResumeAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default);

    Task<CustomerSubscription> CancelAsync(string? ownerReference, int subscriptionId, string? reason,
        bool endOfPeriod, CancellationToken ct = default);

    Task<CustomerSubscription> ReactivateAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default);
}
