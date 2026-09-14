using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed transport over the Maxio Advanced Billing REST API. Every member maps 1:1 to an
/// operation declared in maxio-spec/openapi.yaml; the operationId is named in each doc comment.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// listProductsForProductFamily - GET /product_families/{product_family_id}/products.json.
    /// <paramref name="productFamilyIdOrHandle"/> is either the numeric id or the handle prefixed
    /// with "handle:" (per the spec's path parameter description).
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle, int page, int perPage, bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>listProducts - GET /products.json (all products on the site).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        int page, int perPage, bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>readSite - GET /site.json (site level settings such as the invoicing architecture).</summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>readCustomerByReference - GET /customers/lookup.json?reference=... Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>createCustomer - POST /customers.json.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>listCustomerSubscriptions - GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>createSubscription - POST /subscriptions.json.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>findSubscription - GET /subscriptions/lookup.json?reference=... Returns null when no subscription matches.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
