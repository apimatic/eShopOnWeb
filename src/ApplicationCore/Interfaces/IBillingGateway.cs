using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the external billing system of record (Maxio Advanced Billing).
/// Everything the eShopOnWeb subscription capability needs, expressed in application
/// language; the shapes of the calls it performs are dictated by maxio-spec/openapi.yaml.
/// Implementations live in Infrastructure so that ApplicationCore stays free of transport concerns.
/// </summary>
public interface IBillingGateway
{
    /// <summary>
    /// Plans (products) of the configured product family, plus the usage components published on it.
    /// </summary>
    Task<BillingCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks the customer up by its application reference. Returns null when no customer exists yet.
    /// </summary>
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer for <paramref name="profile"/>, creating it when it does not exist yet.
    /// Concurrent/repeated calls for the same reference resolve to a single Maxio customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(BillingProfile profile, CancellationToken cancellationToken = default);

    /// <summary>All subscriptions that belong to a Maxio customer.</summary>
    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer on a plan.</summary>
    Task<UserSubscription> EnrollAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>Re-reads one enrollment from the billing system (used to confirm state after signup).</summary>
    Task<UserSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
