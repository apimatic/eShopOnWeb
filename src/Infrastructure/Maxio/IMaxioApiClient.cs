using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful transport over the Maxio Advanced Billing API. Each member maps to exactly one
/// operation in maxio-spec/openapi.yaml; no behaviour beyond transport, serialisation and error
/// translation belongs here.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>readSite</c> - <c>GET /site.json</c>. Used for the site's primary currency.
    /// </summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listProductsForProductFamily</c> - <c>GET /product_families/{product_family_id}/products.json</c>.
    /// Follows pagination until the family is exhausted.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the family's numeric id or its handle prefixed with <c>handle:</c>, as the path
    /// parameter is documented in the specification.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>readCustomerByReference</c> - <c>GET /customers/lookup.json?reference=...</c>.
    /// Returns <see langword="null"/> when no customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createCustomer</c> - <c>POST /customers.json</c>. Fails with HTTP 422 when the reference is
    /// already taken, which is what makes the create safe to repeat.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>listCustomerSubscriptions</c> - <c>GET /customers/{customer_id}/subscriptions.json</c>.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>findSubscription</c> - <c>GET /subscriptions/lookup.json?reference=...</c>.
    /// Returns <see langword="null"/> when no subscription carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>createSubscription</c> - <c>POST /subscriptions.json</c>.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
