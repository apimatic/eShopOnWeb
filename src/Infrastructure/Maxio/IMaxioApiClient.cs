using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The slice of the Maxio Billing API this integration uses. Every method throws
/// <see cref="MaxioApiException"/> when the call does not succeed; lookups that are allowed to
/// come back empty return null instead.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /site.json - currency and invoicing configuration of the site.</summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/handle:{handle}/products.json - every page of it.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... - null when no customer carries the reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, string? uniquenessToken, CancellationToken cancellationToken = default);
}
