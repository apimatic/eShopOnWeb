using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, one-to-one binding over the Maxio Advanced Billing REST endpoints this integration uses.
/// </summary>
/// <remarks>
/// Every method maps to exactly one documented Maxio operation and performs no orchestration; the
/// find-or-create and idempotency rules live in <see cref="MaxioSubscriptionService"/>.
/// Failures surface as <see cref="MaxioApiException"/>.
/// </remarks>
public interface IMaxioApiClient
{
    /// <summary><c>GET /site.json</c>. Used for the site's currency.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /product_families/handle:{handle}/products.json</c>.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/lookup.json?reference=</c>. Null when no customer has that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{customerId}/subscriptions.json</c>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary><c>GET /subscriptions/lookup.json?reference=</c>. Null when no subscription has that reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);
}
