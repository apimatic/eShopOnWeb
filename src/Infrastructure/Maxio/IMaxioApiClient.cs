using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing HTTP API. Every endpoint, parameter and payload in this
/// client is implemented against the Maxio OpenAPI specification shipped in <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families.json (listProductFamilies).</summary>
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{product_family_id}/products.json (listProductsForProductFamily).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>GET /products/handle/{api_handle}.json (readProductByHandle). Returns null when not found.</summary>
    Task<MaxioProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference= (readCustomerByReference). Returns null when not found.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer). Throws <see cref="MaxioApiException"/> on validation errors.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json?reference= (findSubscription). Returns null when not found.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription). Throws <see cref="MaxioApiException"/> on validation errors.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
