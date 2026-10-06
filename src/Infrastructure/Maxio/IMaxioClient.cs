using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing HTTP API.
/// Every method maps 1:1 to an operation in the Maxio OpenAPI specification
/// (maxio-spec/openapi.yaml), which is the authoritative contract.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /customers/lookup.json?reference={reference} — Read Customer by Reference. Returns null on 404.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — Create Customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /products.json — List Products (paginated).</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — Create Subscription.</summary>
    /// <param name="paymentCollectionMethod">
    /// Optional payment_collection_method (Collection-Method enum from the spec:
    /// "automatic" or "remittance"). Null lets Maxio apply its site default.
    /// </param>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, string? paymentCollectionMethod = null, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — List Customer Subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}