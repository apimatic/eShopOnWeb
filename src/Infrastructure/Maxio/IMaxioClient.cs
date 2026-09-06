using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing API this application talks to. Every member maps 1:1 to
/// an operation in the Maxio OpenAPI specification; the <c>operationId</c> is named in the docs.
/// </summary>
public interface IMaxioClient
{
    /// <summary><c>readSite</c> — <c>GET /site.json</c>.</summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> — <c>GET /product_families/{product_family_id}/products.json</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the product family id or its handle prefixed with <c>handle:</c>, per the specification.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readProductByHandle</c> — <c>GET /products/handle/{api_handle}.json</c>. Null when not found.
    /// </summary>
    Task<MaxioProduct?> ReadProductByHandleAsync(string apiHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> — <c>GET /customers/lookup.json</c>. Null when no customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createCustomer</c> — <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> — <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> — <c>GET /subscriptions/lookup.json</c>. Null when no subscription carries the reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createSubscription</c> — <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
