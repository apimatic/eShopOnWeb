using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, spec-faithful client over the Maxio Advanced Billing HTTP API. Every member maps to one
/// operation of <c>maxio-spec/openapi.yaml</c> and carries no eShopOnWeb business rules.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /site.json</c> (operation <c>readSite</c>). Used for the site currency.
    /// </summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /product_families/{product_family_id}/products.json</c>
    /// (operation <c>listProductsForProductFamily</c>), paged until exhausted.
    /// </summary>
    /// <param name="productFamilyIdOrHandle">
    /// Either the family's id or its handle prefixed with <c>handle:</c>, as the spec requires.
    /// </param>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdOrHandle,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference=...</c> (operation <c>readCustomerByReference</c>).
    /// Returns null when no customer carries that reference.
    /// </summary>
    Task<MaxioCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /customers.json</c> (operation <c>createCustomer</c>).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/{customer_id}/subscriptions.json</c> (operation <c>listCustomerSubscriptions</c>).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /subscriptions/lookup.json?reference=...</c> (operation <c>findSubscription</c>).
    /// Returns null when no subscription carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>POST /subscriptions.json</c> (operation <c>createSubscription</c>).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
