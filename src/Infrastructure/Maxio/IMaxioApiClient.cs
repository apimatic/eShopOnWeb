using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful client over the Maxio Advanced Billing HTTP API. Every member maps to exactly
/// one operation in <c>maxio-spec/openapi.yaml</c> and is named after that operation's
/// <c>operationId</c>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary><c>GET /site.json</c> - <c>readSite</c>.</summary>
    Task<MaxioSiteResponse> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /product_families/{product_family_id}/products.json</c> - <c>listProductsForProductFamily</c>.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// The spec accepts either the product family's id or its handle prefixed with <c>handle:</c>.
    /// </param>
    Task<IReadOnlyList<MaxioProductResponse>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json</c> - <c>readCustomerByReference</c>. Returns null when the
    /// provider answers <c>404 Not Found</c>, i.e. no customer carries that reference.
    /// </summary>
    Task<MaxioCustomerResponse?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c> - <c>createCustomer</c>.</summary>
    Task<MaxioCustomerResponse> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{customer_id}/subscriptions.json</c> - <c>listCustomerSubscriptions</c>.</summary>
    Task<IReadOnlyList<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /subscriptions/lookup.json</c> - <c>findSubscription</c>. Returns null when the provider
    /// answers <c>404 Not Found</c>.
    /// </summary>
    Task<MaxioSubscriptionResponse?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c> - <c>createSubscription</c>.</summary>
    Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
