using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Model;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin, faithful binding of the Advanced Billing REST endpoints this integration uses. It maps
/// HTTP to types and nothing more - every policy decision (idempotency, plan selection, caching)
/// lives in <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /site.json - site currency and invoicing architecture.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// GET /product_families.json, resolved to the family with the given handle.
    /// The handle is not accepted as a path segment, so the family must be looked up by listing.
    /// Returns null when the site publishes no such family.
    /// </summary>
    Task<MaxioProductFamily?> FindProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>GET /product_families/{product_family_id}/products.json - all pages, archived products excluded.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference=... - null when no customer carries that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json. Throws with <see cref="MaxioApiException.IsReferenceConflict"/> when the reference is taken.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>GET /subscriptions/lookup.json?reference=... - null when no subscription carries that reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json. Throws with <see cref="MaxioApiException.IsReferenceConflict"/> when the reference is taken.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);
}
