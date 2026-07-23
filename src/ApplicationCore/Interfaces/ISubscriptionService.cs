using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface consumed by the storefront and the PublicApi. Mirrors
/// <see cref="IOrderService"/>: hosts orchestrate, this service owns the rules and the eventing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrolls the eShopOnWeb user in a plan, creating the provider customer if needed.
    /// Idempotent: if the user already has a live subscription it is returned unchanged.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The eShopOnWeb user's subscriptions as the provider currently reports them.</summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>The user's single live subscription, or null when they have none.</summary>
    Task<Subscription?> GetLiveSubscriptionForUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage against the user's live subscription.</summary>
    Task<UsageReport> RecordUsageAsync(string userName, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — the prorated cost of a plan change, shown before the customer confirms.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userName, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 step 4 — commits a plan change. <paramref name="previewedPaymentDueInCents"/> is the amount
    /// the customer was shown; the commit is rejected if the provider no longer quotes that amount.
    /// </summary>
    Task<Subscription> ChangePlanAsync(string userName, string targetPlanHandle, PlanChangeTiming timing, int? previewedPaymentDueInCents, CancellationToken cancellationToken = default);

    /// <summary>UC4 — pauses the user's subscription.</summary>
    Task<Subscription> PauseAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>UC4 — resumes the user's paused subscription.</summary>
    Task<Subscription> ResumeAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancels the user's subscription immediately or at the end of the period.</summary>
    Task<Subscription> CancelAsync(string userName, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivates the user's cancelled subscription.</summary>
    Task<Subscription> ReactivateAsync(string userName, CancellationToken cancellationToken = default);
}
