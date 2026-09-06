using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The subset of the Maxio Advanced Billing API this integration consumes. Every member maps to a
/// single operation in the Maxio OpenAPI specification, named after that operation's <c>operationId</c>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Maxio operation <c>readSite</c>: <c>GET /site.json</c>.</summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Maxio operation <c>listProductsForProductFamily</c>:
    /// <c>GET /product_families/{product_family_id}/products.json</c>, addressing the family by handle.
    /// Follows pagination and returns every page.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maxio operation <c>readCustomerByReference</c>: <c>GET /customers/lookup.json</c>.
    /// Returns <c>null</c> when no customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Maxio operation <c>createCustomer</c>: <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maxio operation <c>listCustomerSubscriptions</c>:
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maxio operation <c>findSubscription</c>: <c>GET /subscriptions/lookup.json</c>.
    /// Returns <c>null</c> when no subscription carries the reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>Maxio operation <c>createSubscription</c>: <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
