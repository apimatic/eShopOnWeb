using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed access to the Maxio Advanced Billing operations this integration uses. Every member maps
/// to exactly one operation in the Maxio OpenAPI specification (maxio-spec/openapi.yaml); the
/// operationId and path are named on each.
/// </summary>
/// <remarks>
/// Implementations throw <see cref="MaxioApiException"/> for non-success responses, except where a
/// member documents that it returns null for a 404.
/// </remarks>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>readSite</c> — GET /site.json.
    /// </summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> — GET /product_families/{product_family_id}/products.json,
    /// walking every page. The path parameter accepts "either the product family's id or its handle
    /// prefixed with <c>handle:</c>", so this addresses the family by handle.
    /// </summary>
    /// <exception cref="MaxioApiException">404 when no family has that handle.</exception>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readProductByHandle</c> — GET /products/handle/{api_handle}.json.
    /// Returns null when no product has that handle.
    /// </summary>
    Task<MaxioProduct?> ReadProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> — GET /customers/lookup.json?reference=...
    /// Returns null when no customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createCustomer</c> — POST /customers.json.
    /// </summary>
    /// <exception cref="MaxioApiException">
    /// 422 when the reference is already taken — the signal that a concurrent request won the race.
    /// </exception>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> — GET /customers/{customer_id}/subscriptions.json.
    /// Returns an empty list when the customer no longer exists.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createSubscription</c> — POST /subscriptions.json.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscription subscription,
        CancellationToken cancellationToken = default);
}
