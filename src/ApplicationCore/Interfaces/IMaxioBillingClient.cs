using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin client over the Maxio Advanced Billing HTTP API, scoped to the operations the
/// subscription-billing capability needs. Every method maps 1:1 to an operation documented
/// in maxio-spec/openapi.yaml - that spec is the contract, this interface does not add,
/// rename, or reinterpret anything beyond it.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference} - null when no match (404).</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json?reference={reference} - null when no match (404).</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
