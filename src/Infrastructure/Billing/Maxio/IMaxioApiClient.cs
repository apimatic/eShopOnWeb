using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The subset of the Maxio Advanced Billing REST API this integration uses, expressed one method per
/// endpoint. Implementations translate transport failures into <see cref="MaxioApiException"/>;
/// "not found" lookups come back as <c>null</c> rather than as an exception.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /site.json -- reads the site, primarily for its currency.</summary>
    Task<MaxioSite> ReadSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /product_families.json, matched on handle. Returns <c>null</c> when no family has the handle.</summary>
    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/{id}/products.json -- the products offered as plans.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(long productFamilyId, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... Returns <c>null</c> when no customer carries the reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{id}/subscriptions.json -- every subscription the customer holds, live or not.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
