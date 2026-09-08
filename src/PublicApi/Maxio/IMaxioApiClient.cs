using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing API surface used by the subscription feature.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Looks a customer up by reference (GET /customers/lookup.json). Returns <c>null</c> when absent.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Creates a customer (POST /customers.json).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes customer, CancellationToken cancellationToken);

    /// <summary>Reads a product by handle (GET /products/handle/{handle}.json). Returns <c>null</c> when absent.</summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>Lists all non-archived products on the site (GET /products.json, paged).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>Creates a subscription (POST /subscriptions.json).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionAttributes subscription, string? uniquenessToken, CancellationToken cancellationToken);

    /// <summary>Lists a customer's subscriptions (GET /customers/{id}/subscriptions.json).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
