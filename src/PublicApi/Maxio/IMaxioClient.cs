using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client over the Maxio (Advanced Billing) REST API.
/// All members hit Maxio directly; lookups return <see langword="null"/> when the
/// upstream resource does not exist (HTTP 404).
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /site.json — returns the site's billing currency.</summary>
    Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// GET /customers/lookup.json?reference=... — finds a customer by its reference.
    /// Returns <see langword="null"/> when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json — creates a new customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerBody body, CancellationToken cancellationToken);

    /// <summary>
    /// GET /product_families/handle:{family}/products.json — lists the products in a
    /// product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken);

    /// <summary>
    /// GET /products/handle/{api_handle}.json — reads a single product by its API
    /// handle. Returns <see langword="null"/> when the product does not exist.
    /// </summary>
    Task<MaxioProduct?> FindProductByHandleAsync(string apiHandle, CancellationToken cancellationToken);

    /// <summary>
    /// GET /subscriptions/lookup.json?reference=... — finds a subscription by its
    /// reference. Returns <see langword="null"/> when no subscription matches.
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json — creates a new subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionBody body, CancellationToken cancellationToken);

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json — lists all subscriptions that
    /// belong to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
