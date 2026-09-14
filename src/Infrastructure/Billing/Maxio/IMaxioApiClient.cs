using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin, transport-level access to the Maxio Advanced Billing (Billing API) endpoints this
/// integration uses. It owns HTTP, authentication, paging and error translation; all policy
/// (idempotency, caching, mapping) lives above it in <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{handle}/products.json - every page.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... - null when no customer carries that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>GET /site.json</summary>
    Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken = default);
}
