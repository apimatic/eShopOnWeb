using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed, low-level client over the Maxio Advanced Billing REST API. Each method maps to a
/// single operation in the OpenAPI spec (maxio-spec/openapi.yaml) and throws
/// <see cref="MaxioApiException"/> on non-success responses.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (listProductsForProductFamily).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference= (readCustomerByReference). Returns null on 404.</summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json (createCustomer).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json (listCustomerSubscriptions).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json (createSubscription).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
