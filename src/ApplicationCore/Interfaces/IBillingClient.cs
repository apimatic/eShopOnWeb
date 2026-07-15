using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the billing provider (Maxio Advanced Billing). This is the single
/// integration point with the provider (plan.md §2.2/§4.2) — ApplicationCore depends only on this
/// interface and the plain models in <c>Entities.SubscriptionAggregate</c>, never on the provider's
/// SDK types or HttpClient. The one concrete implementation lives in Infrastructure.
/// </summary>
public interface IBillingClient
{
    /// <summary>UC1 step 1 / UC0 verify — the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1 step 5 duplicate-detection — every subscription for the customer keyed by this stable reference (empty if the customer does not exist yet).</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>UC1 steps 3-4 — find-or-create the customer keyed by <paramref name="customerReference"/>, then enroll them in <paramref name="productHandle"/>.</summary>
    Task<Subscription> CreateSubscriptionAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Pre-flight / post-conflict refresh read used by UC2-UC4.</summary>
    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC2 precondition — throws <see cref="Exceptions.BillingConfigurationException"/> if the configured metered-component handle does not resolve to a metered-kind component on the configured family.</summary>
    Task EnsureMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>UC2 steps 2-3 — record a metered quantity against the configured component and report the period-to-date total.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 2 — the prorated charge/credit (or, for <see cref="PlanChangeTiming.AtNextRenewal"/>, the flat new-plan price) for a candidate plan change.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string currentProductHandle, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>UC3 step 4 — commit a previously previewed plan change.</summary>
    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
