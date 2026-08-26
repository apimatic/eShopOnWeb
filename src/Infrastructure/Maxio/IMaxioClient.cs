using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Low-level client for the Maxio Advanced Billing API. Every method maps to an endpoint in the
/// OpenAPI specification (maxio-spec/openapi.yaml), which is the authoritative contract.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /customers/lookup.json?reference={reference} (readCustomerByReference). Null when no match (404).</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{product_family_id}/products.json (listProductsForProductFamily), addressed by family handle.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
