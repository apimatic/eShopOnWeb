using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// The subset of the Maxio Advanced Billing API this integration consumes. Every member maps one-to-one
/// onto an operation declared in the OpenAPI specification under <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /site.json</c> (<c>readSite</c>). Reads the site's currency and invoicing architecture.
    /// </summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /product_families/{product_family_id}/products.json</c> (<c>listProductsForProductFamily</c>),
    /// following pagination until the family is exhausted.
    /// </summary>
    /// <param name="productFamilyId">Either the family's id or its handle prefixed with <c>handle:</c>.</param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json</c> (<c>readCustomerByReference</c>). Returns <c>null</c> when no
    /// customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /customers.json</c> (<c>createCustomer</c>).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/{customer_id}/subscriptions.json</c> (<c>listCustomerSubscriptions</c>).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /subscriptions.json</c> (<c>createSubscription</c>).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
