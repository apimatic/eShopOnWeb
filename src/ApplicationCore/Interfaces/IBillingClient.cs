using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic abstraction over the billing provider (§2.2/§4.2 — the single integration
/// point with the billing provider). ApplicationCore depends only on this interface; the concrete
/// implementation (Maxio, in Infrastructure) is the sole place the provider is ever touched.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Validates that the configured product family, plans, and metered component resolve to the
    /// shape the integration expects (metered component is of metered kind, plans exist and do not
    /// require a payment method). Throws <see cref="Exceptions.BillingConfigurationException"/> if
    /// not — called at startup and lazily before the first usage call (UC2 preconditions).
    /// </summary>
    Task EnsureCatalogConfiguredAsync(CancellationToken ct = default);

    /// <summary>Lists the plans available in the configured product family (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>Resolves a single plan by its handle, or null if it does not exist/is archived.</summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string productHandle, CancellationToken ct = default);

    /// <summary>
    /// Returns the provider customer id for <paramref name="customerReference"/>, creating the
    /// customer if none exists yet. Idempotent on the reference (UC1 step 3).
    /// </summary>
    Task<int> GetOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default);

    /// <summary>
    /// Looks up the provider customer id for <paramref name="customerReference"/> without creating
    /// one. Returns null when no customer exists yet for that reference.
    /// </summary>
    Task<int?> FindCustomerByReferenceAsync(string customerReference, CancellationToken ct = default);

    /// <summary>All subscriptions for a provider customer (double-enrollment guard + "my subscriptions" read).</summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int providerCustomerId, CancellationToken ct = default);

    /// <summary>Enrolls a customer in a plan, without any payment-method capture (UC1 step 4).</summary>
    Task<Subscription> CreateSubscriptionAsync(int providerCustomerId, string productHandle, CancellationToken ct = default);

    /// <summary>Reads the current provider-side state of a subscription (used to re-check state before retries).</summary>
    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Records a unit (or batch) of metered usage against the configured component (UC2).</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken ct = default);

    /// <summary>Previews the prorated cost of an immediate plan change (UC3 step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Commits an immediate, prorated plan change (UC3 "apply now with proration").</summary>
    Task<Subscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Schedules a plan change for the next renewal, with no proration (UC3 "apply at next renewal").</summary>
    Task<Subscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default);

    /// <summary>Places the subscription on hold (UC4 pause).</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Resumes a subscription that is on hold (UC4 resume).</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current period (UC4 cancel).</summary>
    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken ct = default);

    /// <summary>Reactivates a canceled subscription (UC4 reactivate).</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
