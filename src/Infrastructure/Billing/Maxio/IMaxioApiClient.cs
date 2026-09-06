using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing REST API this integration depends on.
/// Every member maps to one documented endpoint; failures surface as <see cref="MaxioApiException"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /site.json</c> — the site's own record, used for its trading currency.
    /// </summary>
    Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /product_families/handle:{handle}/products.json</c> — every product in a family,
    /// following pagination.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference=…</c> — returns null when no customer carries the
    /// reference (Maxio answers 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c></summary>
    Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{customerId}/subscriptions.json</c></summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c></summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default);
}
