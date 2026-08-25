using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level client for the Maxio Advanced Billing API. Every method maps 1:1 to an
/// operation in the Maxio OpenAPI specification (maxio-spec/openapi.yaml), which is the
/// authoritative contract for paths, parameters, schemas and auth.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (handle: prefix supported per spec).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference}. Returns null when Maxio responds 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (identified by product_handle + customer_id, both per spec).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
