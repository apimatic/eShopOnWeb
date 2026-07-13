using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the recurring-billing provider (§2.2). The only implementation is
/// <c>MaxioBillingClient</c> in Infrastructure — ApplicationCore never references the Maxio SDK
/// or <see cref="System.Net.Http.HttpClient"/> directly, only this interface.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the plans available for subscription (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the configured metered component resolves and is of metered kind (UC2 preconditions).
    /// Throws <see cref="Exceptions.BillingConfigurationException"/> if it does not.
    /// </summary>
    Task ValidateMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the provider-side customer id for <paramref name="customerReference"/>, without creating one. Returns null if none exists.</summary>
    Task<long?> TryFindCustomerIdAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider-side customer for <paramref name="customerReference"/>, or creates one (UC1 step 3).</summary>
    Task<long> FindOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to the given provider customer id (UC1 step 4 idempotency check, UC1/UC4 read model).</summary>
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls the customer in the given plan (UC1 step 4).</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription's current state from the provider (used before/after every lifecycle transition).</summary>
    Task<BillingSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records usage against the configured metered component on the subscription (UC2 step 2).</summary>
    Task<long> RecordUsageAsync(long subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the period-to-date usage total for the configured metered component (UC2 step 3). Returns null if the read-back fails.</summary>
    Task<int?> TryGetPeriodToDateUsageAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Previews the prorated cost of moving the subscription to <paramref name="targetProductHandle"/>, effective immediately (UC3 step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(long subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    /// <summary>Commits an immediate, prorated plan change (UC3 step 4). "At next renewal" timing is not exposed by the SDK — see <see cref="Exceptions.PlanChangeNotSupportedException"/>.</summary>
    Task<BillingSubscription> CommitPlanChangeAsync(long subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelSubscriptionAsync(long subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
