using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin transport over the Maxio Advanced Billing REST API: one method per endpoint, no business
/// rules. Every method throws <see cref="MaxioApiException"/> on a non-success response, except
/// where the contract explicitly returns null for "not found".
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /site.json - used for the site's billing currency.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /product_families/handle:{handle}/products.json, following pagination to the end.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference=... Returns null when no customer carries that
    /// reference (Maxio answers 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer,
        CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscriptionAttributes subscription,
        CancellationToken cancellationToken = default);
}
