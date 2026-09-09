using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed surface of the Maxio Advanced Billing API used by eShopOnWeb.
/// Every operation maps 1:1 to an operation defined in maxio-spec/openapi.yaml
/// (the authoritative contract); the operationId is noted per method.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// operationId: listProductsForProductFamily
    /// GET /product_families/{product_family_id}/products.json — the path
    /// parameter accepts the family handle prefixed with "handle:".
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: readCustomerByReference
    /// GET /customers/lookup.json?reference={reference}
    /// Returns null on 404 (no customer with that reference).
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: createCustomer
    /// POST /customers.json
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: findSubscription
    /// GET /subscriptions/lookup.json?reference={reference}
    /// Returns null on 404 (no subscription with that reference).
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: createSubscription
    /// POST /subscriptions.json
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: listCustomerSubscriptions
    /// GET /customers/{customer_id}/subscriptions.json
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// operationId: readSubscription
    /// GET /subscriptions/{subscription_id}.json
    /// </summary>
    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
