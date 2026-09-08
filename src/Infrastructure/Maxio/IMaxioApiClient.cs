using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing API, built against the
/// OpenAPI contract in maxio-spec/openapi.yaml. Only the operations needed for
/// eShopOnWeb subscriptions are exposed.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// Lists all non-archived products of the site (GET /products.json), following pagination.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a customer by its app-side reference (GET /customers/lookup.json?reference=...).
    /// Returns null when no customer matches (HTTP 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer (POST /customers.json).
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription (POST /subscriptions.json).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionBody subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a subscription by its app-side reference (GET /subscriptions/lookup.json?reference=...).
    /// Returns null when no subscription matches (HTTP 404).
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer
    /// (GET /customers/{customer_id}/subscriptions.json).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
