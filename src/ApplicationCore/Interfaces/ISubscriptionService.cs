using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (plan.md UC1–UC4). Mirrors <see cref="IOrderService"/>:
/// hosts orchestrate through this, never through <see cref="IBillingClient"/> directly.
/// </summary>
/// <remarks>
/// <para>
/// Methods that act on an existing subscription take an <c>actingUserReference</c>. When it is
/// non-null the subscription must belong to that user or the call fails as if the subscription did
/// not exist — this is what keeps one customer from acting on another's subscription. Passing
/// <c>null</c> means "administrator, any subscription" and must only be done from an
/// administrator-guarded surface.
/// </para>
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can choose from.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1 — every subscription belonging to an eShopOnWeb user.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForUserAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — enrols the user in a plan, creating the provider-side customer if needed.
    /// Idempotent: if the user already has an active subscription, that one is returned rather
    /// than a second enrolment being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference,
        string productHandle,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — records metered usage and reads back the period-to-date total.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId,
        int quantity,
        string? memo,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records one unit of usage against every active subscription the user holds.
    /// Used by the order-placed hook, where there may be no subscription at all; returns an empty
    /// list in that case rather than failing.
    /// </summary>
    Task<IReadOnlyList<UsageReport>> RecordUsageForUserAsync(string userReference,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC3 — what a plan change would cost, without changing anything.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 — commits a previously previewed plan change. <paramref name="confirmedFingerprint"/>
    /// is the <see cref="PlanChangePreview.Fingerprint"/> the customer was shown; the change is
    /// rejected if the cost has moved since.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        string confirmedFingerprint,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — places the subscription on hold.</summary>
    Task<Subscription> PauseAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — takes the subscription off hold.</summary>
    Task<Subscription> ResumeAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — cancels the subscription, immediately or at the end of the period.</summary>
    Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC4 — reactivates a cancelled or expired subscription.</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, enforcing ownership the same way the actions do.</summary>
    Task<Subscription> GetSubscriptionAsync(int subscriptionId,
        string? actingUserReference,
        CancellationToken cancellationToken = default);
}
