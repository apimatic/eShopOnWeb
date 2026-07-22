using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and the recurring-billing provider.
/// Exactly one Infrastructure implementation talks to the provider; nothing else does.
/// Implementations normalize provider payloads (money is always in major currency units) and
/// translate every failure into <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>The configured entities the integration operates against (the UC0 seed).</summary>
    BillingCatalog Catalog { get; }

    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a plan by its durable handle, or null when the handle does not resolve.</summary>
    Task<BillingPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a component by its durable handle, or null when the handle does not resolve.</summary>
    Task<BillingComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads the customer carrying the supplied reference, or null when there is none.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer for the supplied eShopOnWeb user reference, creating one if needed.
    /// Idempotent on the reference, so a retried subscribe never creates a second customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string userReference, string? email, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a customer; empty when there are none.</summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription, or null when the id is unknown to the provider.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Reports metered usage against a subscription's component.</summary>
    Task<UsageReceipt> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the running period-to-date unit total for a subscription's component.</summary>
    Task<decimal?> GetUsageTotalAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Computes the prorated cost of moving a subscription onto another plan now.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a subscription onto another plan — immediately with proration, or deferred to the
    /// next renewal when <paramref name="applyImmediately"/> is false.
    /// </summary>
    Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately, or at the end of the current period.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
