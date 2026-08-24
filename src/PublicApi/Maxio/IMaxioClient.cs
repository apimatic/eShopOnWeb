using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API. Every method maps to an endpoint
/// from the OpenAPI specification in maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (handle: prefix supported per spec).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference}. Returns null when no customer matches (404).</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
