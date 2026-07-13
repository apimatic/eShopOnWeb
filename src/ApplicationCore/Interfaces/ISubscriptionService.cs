using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// Use-case surface for the subscription feature (mirrors IOrderService): orchestrates the
// billing client, applies eShopOnWeb-side rules (ownership, legal transitions, idempotency),
// and publishes MediatR notifications on state changes.
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<Subscription> SubscribeAsync(string userReference, string email, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);

    Task<SubscriptionUsageResult> RecordUsageAsync(string actingUserReference, bool isAdmin, int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<Subscription> ChangePlanNowAsync(string userReference, int subscriptionId, string targetProductHandle, BillingPlanChangePreview confirmedPreview, CancellationToken cancellationToken = default);

    Task<Subscription> SchedulePlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<Subscription> PauseAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelAsync(string actingUserReference, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default);
}

public class SubscriptionUsageResult
{
    public SubscriptionUsageResult(int quantityRecorded, int? periodToDateTotal)
    {
        QuantityRecorded = quantityRecorded;
        PeriodToDateTotal = periodToDateTotal;
    }

    public int QuantityRecorded { get; }

    // Null when the record succeeded but the read-back of the running total failed (§ UC2 failure scenarios).
    public int? PeriodToDateTotal { get; }
}
