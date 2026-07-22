using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (plan.md §3), mirroring <see cref="IOrderService"/>.
/// Orchestrates the billing client, enforces the domain rules, and publishes the lifecycle
/// notifications; the hosts only translate HTTP in and view models out.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the eShopOnWeb user in a plan. Idempotent: if the user already has a live
    /// subscription on that plan it is returned rather than a second enrolment being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 step 7 / account page — the user's subscriptions.</summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription; null when the id is unknown.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered consumption and returns the running period-to-date total.</summary>
    Task<UsageSummary> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2 — the running period-to-date usage without recording anything.</summary>
    Task<UsageSummary> GetUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — what the plan change would cost.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits the plan change. When <paramref name="confirmedPreview"/> is supplied it
    /// is re-checked against the provider and the commit is refused if the quote has changed.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        PlanChangePreview? confirmedPreview,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — pause.</summary>
    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — resume.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancel, immediately or at the end of the period.</summary>
    Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivate.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
