using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (plan.md UC1–UC4), mirroring <see cref="IOrderService"/>'s role for
/// the one-time purchase flow. It orchestrates the billing seam, enforces the domain rules that must not
/// reach the provider, and publishes the in-process lifecycle notifications.
/// </summary>
/// <remarks>
/// Every subscription-scoped operation takes a <c>restrictToUserReference</c>. When it is non-null the
/// service refuses to act on a subscription belonging to anybody else, which is how the storefront keeps
/// a customer inside their own account; admin callers pass <see langword="null"/> to act on any
/// subscription (plan.md §2.4).
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans a customer can subscribe to.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 — ensures a provider-side customer exists for <paramref name="userReference"/> and enrolls them
    /// in <paramref name="planHandle"/>. Idempotent: an existing subscription on that plan is returned
    /// rather than a second enrollment being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 — the subscriptions belonging to an eShopOnWeb user; empty when they have none.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>The user's live subscription, or <see langword="null"/> when they have none.</summary>
    Task<Subscription?> FindActiveSubscriptionAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records metered usage against a subscription and reads back the period-to-date total.
    /// Rejects a non-positive quantity and a subscription that is not live before calling the provider.
    /// </summary>
    Task<UsageSummary> RecordUsageAsync(int subscriptionId, int quantity, string? memo,
        string? restrictToUserReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2 — records usage against the user's own live subscription. Returns <see langword="null"/> when
    /// the user has no live subscription, so best-effort callers (the order-placed hook) never fail an
    /// eShopOnWeb flow because of billing.
    /// </summary>
    Task<UsageSummary?> RecordUsageForUserAsync(string userReference, int quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>UC2 — the period-to-date usage on a subscription, without recording anything.</summary>
    Task<UsageSummary?> GetUsageSummaryAsync(int subscriptionId, string? restrictToUserReference,
        CancellationToken cancellationToken = default);

    /// <summary>UC3 — the prorated cost of a plan change. Charges nothing.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        string? restrictToUserReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 — commits a plan change. <paramref name="previewedNetAmount"/> is the net amount the customer
    /// confirmed; the service re-previews and rejects the commit if the amount has moved, so a customer is
    /// never charged something other than what they were shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        decimal previewedNetAmount, string? restrictToUserReference, CancellationToken cancellationToken = default);

    /// <summary>UC4 — applies a lifecycle transition, rejecting one that is illegal from the current state.</summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, string? reason,
        string? restrictToUserReference, CancellationToken cancellationToken = default);
}
