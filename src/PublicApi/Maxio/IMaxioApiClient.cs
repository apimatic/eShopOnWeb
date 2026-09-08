using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Contracts;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin client over the Maxio Advanced Billing HTTP API. Every endpoint, path/query
/// parameter, and payload shape used here is taken from the Maxio OpenAPI specification
/// (maxio-spec/openapi.yaml). Auth is HTTP Basic where the username is the API key.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json — list products in a family (by handle).</summary>
    Task<System.Collections.Generic.IReadOnlyList<Product>> ListProductsInFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /products/handle/{api_handle}.json — read a single product by handle.</summary>
    Task<Product?> FindProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... — read a customer by its reference. Null when not found (404).</summary>
    Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — create a customer.</summary>
    Task<Customer> CreateCustomerAsync(CustomerAttributes attributes, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — list all subscriptions for a customer.</summary>
    Task<System.Collections.Generic.IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — create a subscription for an existing customer on a product handle.</summary>
    Task<Subscription> CreateSubscriptionAsync(
        string productHandle, int customerId, DateTimeOffset? nextBillingAt, CancellationToken cancellationToken = default);
}
