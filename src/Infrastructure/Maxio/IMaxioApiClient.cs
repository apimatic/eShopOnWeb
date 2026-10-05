using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport client for the Maxio Advanced Billing API. Every method
/// maps 1:1 to an operation of the authoritative OpenAPI spec
/// (maxio-spec/openapi.yaml) - endpoints, query parameters and request /
/// response envelopes are taken from the spec, not invented.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /products/handle/{api_handle}.json - Read Product by Handle. Returns null on 404.</summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>GET /products.json - List Products (paginated; pages until exhausted).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... - Read Customer by Reference. Returns null on 404.</summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json - Create Customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest customer, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json - Create Subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/{subscription_id}.json - Read Subscription. Returns null on 404.</summary>
    Task<MaxioSubscription?> GetSubscriptionByIdAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json?reference=... - Find Subscription. Returns null on 404.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json - List Customer Subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}