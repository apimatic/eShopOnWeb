using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Low-level client for the Maxio Advanced Billing API.
/// Endpoint shapes follow the published API reference (Basic auth, .json suffixes).
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... Returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customerId}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/lookup.json?reference=... Returns null when no subscription matches.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionRequestItem subscription, CancellationToken cancellationToken = default);
}
