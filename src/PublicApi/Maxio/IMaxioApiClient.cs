using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the subset of the Maxio Advanced Billing API used by the
/// subscription capability. Endpoints, query parameters, payloads and response
/// shapes are all taken from maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /products.json — List Products.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>GET /site.json — Read Site (used to resolve the billing currency).</summary>
    Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken);

    /// <summary>GET /customers/lookup.json?reference=... — Read Customer by Reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json — Create Customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — List Customer Subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>GET /subscriptions/lookup.json?reference=... — Find Subscription by reference.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json — Create Subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request, CancellationToken cancellationToken);
}
