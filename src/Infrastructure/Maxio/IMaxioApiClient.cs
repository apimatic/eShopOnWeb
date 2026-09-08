using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed operations over the Maxio Advanced Billing HTTP API. Every method
/// maps 1:1 to an operation in the Maxio OpenAPI specification (maxio-spec/),
/// which is the authoritative contract for this integration.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /customers/lookup.json — Read Customer by Reference. Returns null on 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — Create Customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerBody customer, CancellationToken cancellationToken = default);

    /// <summary>GET /products.json — List Products (all pages).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json — Find Subscription by reference. Returns null on 404.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — List Customer Subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — Create Subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionBody subscription, CancellationToken cancellationToken = default);
}
