using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing API this integration uses. One method per HTTP
/// endpoint, with no policy of its own — orchestration and idempotency live a layer above, in
/// <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /product_families/handle:{handle}/products.json</c> — the products of one family,
    /// which is how the subscription plan catalog is sourced.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference={reference}</c> — the customer with that
    /// reference, or <see langword="null"/> when the site has none (Maxio answers 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c> — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCreateCustomer customer,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{id}/subscriptions.json</c> — every subscription of one customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c> — enrolls a customer on a product.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCreateSubscription subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /subscriptions/lookup.json?reference={reference}</c> — the subscription with that
    /// reference, or <see langword="null"/> when there is none.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /site.json</c> — the site's own settings, including its invoicing model.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);
}
