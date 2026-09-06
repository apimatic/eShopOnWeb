using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing API this integration needs, one method per verified
/// endpoint. Implementations throw <see cref="MaxioApiException"/> for any non-success answer.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary><c>GET /site.json</c> - used for the site's default currency.</summary>
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /product_families/handle:{handle}/products.json</c>, following pagination to the end.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference=...</c>. Returns null when Maxio answers 404, which is
    /// how it reports "no customer with that reference".
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{id}/subscriptions.json</c>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /subscriptions/lookup.json?reference=...</c>. Returns null on 404, i.e. no subscription
    /// carries that reference.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
