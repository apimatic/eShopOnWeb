using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, low-level wrapper over the Maxio Advanced Billing REST API. Each method maps to a single
/// Maxio endpoint and returns the modelled wire contract (or throws <see cref="MaxioApiException"/>
/// on an unsuccessful response). Orchestration and idempotency live in <see cref="MaxioBillingService"/>.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken);

    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>GET /customers/{id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, CancellationToken cancellationToken);
}
