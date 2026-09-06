using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The slice of the Maxio Billing API this integration talks to. One method per Maxio endpoint,
/// with no policy of its own - orchestration and idempotency live in the billing service above it.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /site.json</summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/lookup.json?reference=...
    /// Returns null when no customer carries the reference.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerAttributes customer, string uniquenessToken, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionAttributes subscription, string uniquenessToken, CancellationToken cancellationToken = default);
}
