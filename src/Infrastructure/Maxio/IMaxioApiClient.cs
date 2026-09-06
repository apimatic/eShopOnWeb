using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing REST API this integration depends on.
/// Every member maps one-to-one onto a documented endpoint; no business rules live here.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /site.json</summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken);

    /// <summary>GET /product_families.json, paged until exhausted.</summary>
    Task<IReadOnlyList<MaxioProductFamily>> ListProductFamiliesAsync(CancellationToken cancellationToken);

    /// <summary>GET /product_families/{product_family_id}/products.json, paged until exhausted.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference=... Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>GET /subscriptions/lookup.json?reference=... Returns null when no subscription matches.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);
}
