using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam through which eShopOnWeb talks to its billing provider.
/// Implemented in Infrastructure by the one concrete Maxio client (§2.2) — nothing else in the
/// solution talks to the provider directly. All members throw
/// <see cref="Exceptions.BillingProviderException"/> on provider I/O or API failure.
/// </summary>
public interface IBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the configured metered component resolves to a component of metered kind on the
    /// product family (§UC2 startup/first-call validation). Throws otherwise.
    /// </summary>
    Task EnsureMeteredComponentConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the provider-side customer for <paramref name="customerReference"/> if one does not already exist (idempotent).</summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default);

    /// <summary>Returns an empty list if no customer exists yet for <paramref name="customerReference"/> (never subscribed).</summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);

    Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<UsageSummary> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string currentProductHandle, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    Task<Subscription> ApplyPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
