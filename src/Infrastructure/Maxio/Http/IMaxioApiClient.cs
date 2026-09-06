using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// The Maxio Advanced Billing operations eShopOnWeb depends on.
/// </summary>
/// <remarks>
/// Every member maps one-to-one onto an operation in the Maxio OpenAPI specification kept in
/// <c>maxio-spec/openapi.yaml</c>; the specification operation id is named on each method. Nothing
/// outside the specification is called, and the shapes exchanged are the specification schemas
/// transcribed in <see cref="Models"/>.
/// </remarks>
public interface IMaxioApiClient
{
    /// <summary>
    /// Reads the Maxio site the configured credentials belong to.
    /// Specification operation <c>readSite</c>: <c>GET /site.json</c>.
    /// </summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the products of a product family, following pagination to the end.
    /// Specification operation <c>listProductsForProductFamily</c>:
    /// <c>GET /product_families/{product_family_id}/products.json</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the numeric family id or its handle prefixed with <c>handle:</c>, as the
    /// specification defines for the <c>product_family_id</c> path parameter.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the reference the subscribing application assigned to them, or returns
    /// <c>null</c> when there is none.
    /// Specification operation <c>readCustomerByReference</c>: <c>GET /customers/lookup.json</c>.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer.
    /// Specification operation <c>createCustomer</c>: <c>POST /customers.json</c>.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer.
    /// Specification operation <c>listCustomerSubscriptions</c>:
    /// <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a subscription by the reference the subscribing application assigned to it, or returns
    /// <c>null</c> when there is none.
    /// Specification operation <c>findSubscription</c>: <c>GET /subscriptions/lookup.json</c>.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for a customer and a product.
    /// Specification operation <c>createSubscription</c>: <c>POST /subscriptions.json</c>.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
