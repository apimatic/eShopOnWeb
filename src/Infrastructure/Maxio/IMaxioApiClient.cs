using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport over the Maxio Advanced Billing REST API. It owns HTTP concerns only - addressing,
/// authentication, pagination and error mapping - and holds no orchestration logic.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// GET /product_families/handle:{handle}/products.json - every product in the family, all pages.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference={reference} - returns <c>null</c> when no customer carries
    /// that reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customerId}/subscriptions.json - all pages.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /site.json - configuration of the site the credentials belong to.</summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);
}
