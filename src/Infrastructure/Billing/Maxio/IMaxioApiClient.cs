using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A thin, spec-faithful wrapper over the Maxio Advanced Billing HTTP API. Every member maps to
/// exactly one operation in the Maxio OpenAPI specification and performs no business logic, so the
/// contract stays auditable against the specification.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>readSite</c> &#8212; <c>GET /site.json</c>.
    /// </summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> &#8212;
    /// <c>GET /product_families/{product_family_id}/products.json</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the id of the product family or its handle prefixed with <c>handle:</c>, as the
    /// specification requires for this path parameter.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> &#8212; <c>GET /customers/lookup.json?reference=</c>.
    /// Returns <c>null</c> when the site has no customer with that reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createCustomer</c> &#8212; <c>POST /customers.json</c>.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> &#8212;
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> &#8212; <c>GET /subscriptions/lookup.json?reference=</c>.
    /// Returns <c>null</c> when no subscription carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createSubscription</c> &#8212; <c>POST /subscriptions.json</c>.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
