using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful access to the Maxio Advanced Billing API. Each member corresponds one-to-one
/// with an operation declared in <c>maxio-spec/openapi.yaml</c> and is named after its
/// <c>operationId</c>. Non-success responses surface as <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary><c>readSite</c> - <c>GET /site.json</c>.</summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> - <c>GET /product_families/{product_family_id}/products.json</c>.
    /// The specification allows the family to be addressed by id or by <c>handle:my-family</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> - <c>GET /customers/lookup.json?reference=</c>. Returns null on
    /// the 404 Maxio answers with when no customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createCustomer</c> - <c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> - <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> - <c>GET /subscriptions/lookup.json?reference=</c>. Returns null on the
    /// documented 404.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>createSubscription</c> - <c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request,
        CancellationToken cancellationToken = default);
}
