using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin, typed wrapper over the Maxio Advanced Billing REST API. Each method maps to a single documented
/// Maxio endpoint. Higher-level orchestration (idempotency, mapping) lives in <see cref="ISubscriptionService"/>.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer has that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — creates a new customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default);

    /// <summary>GET /product_families/handle:{handle}/products.json — lists the plans in a product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{id}/subscriptions.json — lists all subscriptions for a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — creates a subscription. <paramref name="uniquenessToken"/> guards retries.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken = default);
}
