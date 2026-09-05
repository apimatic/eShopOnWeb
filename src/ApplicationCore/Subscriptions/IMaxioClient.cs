using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Thin client over the Maxio Advanced Billing API, scoped to exactly the operations the
/// subscribe flow needs. Every method maps to an endpoint defined in the maxio-spec/ OpenAPI
/// contract.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/{handle}/products.json - non-archived plans only.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference= - returns null when no match exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for <paramref name="reference"/> (the
    /// eShopOnWeb user name): looks it up first, and only creates one if it does not already
    /// exist. Maxio enforces uniqueness on <paramref name="reference"/>, so a create race is
    /// resolved by re-reading the existing customer.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default);
}
