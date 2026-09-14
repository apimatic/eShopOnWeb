using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. The shapes and endpoints it
/// calls are defined by the Maxio OpenAPI specification (maxio-spec/openapi.yaml),
/// which is the authoritative contract for every interaction.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET /product_families/{product_family_id}/products.json (by family handle)</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... (null when not found)</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>GET /subscriptions/{subscription_id}.json (null when not found)</summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);
}
