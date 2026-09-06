using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin, stateless transport over the Advanced Billing REST API. It maps one method to one
/// documented endpoint and performs no business logic; orchestration lives in
/// <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary><c>GET /site.json</c> — the site the API key belongs to.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary><c>GET /product_families/handle:{handle}/products.json</c> — every product in a family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/lookup.json?reference=</c> — null when no customer carries that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c> — fails with 422 when the reference is already taken.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary><c>GET /subscriptions/lookup.json?reference=</c> — null when no subscription carries that reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c> — fails with 422 when the reference is already taken.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /subscriptions.json?customer_id=</c> — every subscription belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken = default);
}
