using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A thin, faithful client for the Maxio Advanced Billing operations this integration uses. Every
/// method maps to a single operation of the OpenAPI specification in <c>maxio-spec/</c>; method
/// names follow the specification's operation ids.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>listProductsForProductFamily</c>: <c>GET /product_families/{product_family_id}/products.json</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the product family's id or its handle prefixed with <c>handle:</c>.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyIdOrHandle,
        int page, int perPage, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c>: <c>GET /customers/lookup.json?reference=...</c>.
    /// Returns null when no customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default);

    /// <summary><c>createCustomer</c>: <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c>: <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default);

    /// <summary><c>createSubscription</c>: <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary><c>readSite</c>: <c>GET /site.json</c>.</summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);
}
