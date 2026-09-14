using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed wrapper over the Maxio Advanced Billing REST API (maxio-spec/openapi.yaml).
/// Kept separate from <see cref="MaxioSubscriptionService"/> so the subscribe/idempotency
/// business logic can be unit tested without a real HTTP dependency.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /customers/lookup.json - returns null if no customer has this reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer request, CancellationToken cancellationToken);

    /// <summary>GET /products.json (paged internally) - all products across all families.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription request, CancellationToken cancellationToken);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}
