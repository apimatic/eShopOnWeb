using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thin, low-level client over the Maxio Advanced Billing REST API. Each method maps to a
/// single operation in the Maxio OpenAPI spec (maxio-spec/openapi.yaml). Orchestration and
/// mapping to domain models live in <see cref="MaxioSubscriptionBillingService"/>.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>GET /products.json — lists all products for the site.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference=... — returns null when no customer matches.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json — lists a customer's subscriptions.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — creates a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);
}
