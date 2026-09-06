using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Transport-level client for the Maxio Advanced Billing API. Every member maps 1:1 onto an
/// operation of the OpenAPI specification; no business rules live here.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// GET /product_families/{product_family_id}/products.json - the id may be the numeric id
    /// or a handle prefixed with "handle:", per the spec.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json - returns null when no customer carries the reference.</summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json - returns null when no subscription carries the reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
