using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API.
///
/// Every endpoint, parameter, request/response shape and the Basic-auth scheme
/// used here is taken from the Maxio OpenAPI specification in maxio-spec/
/// (openapi.yaml). Handles are referenced with the spec's "handle:&lt;value&gt;"
/// convention for path parameters that accept them.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/{id}.json (readProductFamily).</summary>
    Task<MaxioProductFamily> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{product_family_id}/products.json (listProductsForProductFamily), paged.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference= (readCustomerByReference). Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionData subscription, CancellationToken cancellationToken = default);
}
