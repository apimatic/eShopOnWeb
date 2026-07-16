using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic abstraction over the recurring-billing provider. This is the single seam through
/// which eShopOnWeb talks to a billing provider — ApplicationCore depends only on this interface; the
/// concrete provider (Maxio Advanced Billing) is implemented once, in Infrastructure.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available for subscription (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Verifies the configured metered component still resolves to a component of metered kind on the
    /// product family (UC2 precondition). Throws <see cref="Exceptions.BillingProviderException"/> if it
    /// does not. Implementations should cache a positive result briefly rather than round-tripping on
    /// every call.
    /// </summary>
    Task ValidateMeteredComponentAsync(CancellationToken ct = default);

    /// <summary>Finds the provider-side customer id for an existing customer reference, or null.</summary>
    Task<int?> FindCustomerIdByReferenceAsync(string customerReference, CancellationToken ct = default);

    /// <summary>
    /// Ensures a provider-side customer exists for the given eShopOnWeb user reference, creating one if
    /// necessary (idempotent on <paramref name="customerReference"/>). Returns the provider customer id.
    /// </summary>
    Task<int> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken ct = default);

    /// <summary>
    /// Returns the customer's active (or activating) subscription within the configured product family, if
    /// one already exists — used to make Subscribe idempotent (UC1 "duplicate subscribe" failure scenario).
    /// </summary>
    Task<BillingSubscription?> FindActiveSubscriptionAsync(int customerId, CancellationToken ct = default);

    /// <summary>Enrolls the customer in the given plan (UC1).</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken ct = default);

    /// <summary>Lists every subscription belonging to a provider customer id.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct = default);

    /// <summary>Reads the live state of a single subscription by its provider id.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Records a quantity of usage against the configured metered component (UC2).</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default);

    /// <summary>Reads the current period-to-date metered usage balance for a subscription (UC2).</summary>
    Task<int?> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Previews a plan change (UC3). When <paramref name="applyNow"/> is true, returns the prorated
    /// charge/credit the provider would apply immediately while preserving the current billing period.
    /// When false, no proration applies — the change would take effect at the next renewal.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken ct = default);

    /// <summary>Commits a previously previewed plan change (UC3).</summary>
    Task<BillingSubscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken ct = default);

    /// <summary>Puts a subscription on hold indefinitely (UC4).</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Resumes a subscription that is on hold (UC4).</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current billing period (UC4).</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken ct = default);

    /// <summary>Reactivates a canceled/expired subscription with a new billing period (UC4).</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
