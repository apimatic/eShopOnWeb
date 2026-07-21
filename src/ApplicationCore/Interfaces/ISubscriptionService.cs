using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface consumed by the Web storefront and the PublicApi — mirrors
/// <see cref="IOrderService"/>: it orchestrates <see cref="IBillingClient"/> and publishes the
/// corresponding in-process MediatR notification after a successful provider call.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 — the plans available on the storefront's Plans page.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// UC1 — enrolls <paramref name="userReference"/> in <paramref name="productHandle"/>. Idempotent:
    /// returns the existing subscription if one is already active for that product rather than
    /// creating a duplicate.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);

    /// <summary>All subscriptions the user has ever had, for the "Mine" page.</summary>
    Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);

    /// <summary>The user's single currently-active subscription, or null if they have none.</summary>
    Task<Subscription?> FindActiveSubscriptionAsync(string userReference, CancellationToken ct = default);

    /// <summary>UC2 — records one usage report against the subscription's metered component.</summary>
    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default);

    /// <summary>UC3 — previews the prorated cost/credit of an immediate plan change.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>UC3 — commits a plan change, immediately with proration or at next renewal without it.</summary>
    Task<Subscription> CommitPlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken ct = default);

    /// <summary>UC4 — pause.</summary>
    Task<Subscription> PauseAsync(string userReference, int subscriptionId, CancellationToken ct = default);

    /// <summary>UC4 — resume.</summary>
    Task<Subscription> ResumeAsync(string userReference, int subscriptionId, CancellationToken ct = default);

    /// <summary>UC4 — cancel, immediately or at the end of the current period.</summary>
    Task<Subscription> CancelAsync(string userReference, int subscriptionId, bool endOfPeriod, CancellationToken ct = default);

    /// <summary>UC4 — reactivate.</summary>
    Task<Subscription> ReactivateAsync(string userReference, int subscriptionId, CancellationToken ct = default);
}
