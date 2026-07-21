using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface - mirrors <see cref="IOrderService"/>. Orchestrates the billing
/// client, enforces the eShopOnWeb-side rules (ownership, legal state transitions, stale-preview
/// rejection), and publishes MediatR notifications on state changes.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken = default);

    Task<BillingUsage> RecordUsageAsync(
        string customerReference,
        int subscriptionId,
        double quantity,
        string? memo,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<BillingComponentBalance> GetUsageBalanceAsync(
        string customerReference,
        int subscriptionId,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(
        string customerReference,
        int subscriptionId,
        string targetPlanHandle,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> CommitPlanChangeAsync(
        string customerReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        BillingPlanChangePreview? confirmedPreview,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelAsync(
        string customerReference,
        int subscriptionId,
        bool endOfPeriod,
        string? reason,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default);
}
