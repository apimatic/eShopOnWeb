using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin client over the Maxio Advanced Billing HTTP API. Every operation, path, and payload
/// shape it implements must come from maxio-spec/openapi.yaml.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Read Customer by Reference (GET /customers/lookup.json). Returns null when no match exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Create Customer (POST /customers.json).</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>List Products (GET /products.json), across all product families on the site.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>List Customer Subscriptions (GET /customers/{customer_id}/subscriptions.json).</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Create Subscription (POST /subscriptions.json).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
