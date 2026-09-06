using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The slice of the Maxio Advanced Billing REST API this integration uses. One method per documented
/// endpoint, with no domain logic: translation to domain concepts happens a layer up.
/// </summary>
internal interface IMaxioApiClient
{
    /// <summary>
    /// <c>GET /product_families/handle:{handle}/products.json</c>, following pagination to the end.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /customers/lookup.json?reference=...</c>. Returns <c>null</c> when no customer carries
    /// that reference (Maxio answers 404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /customers.json</c>.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /subscriptions/lookup.json?reference=...</c>. Returns <c>null</c> when no subscription
    /// carries that reference (Maxio answers 404).
    /// </summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>POST /subscriptions.json</c>.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, CancellationToken cancellationToken = default);

    /// <summary><c>GET /customers/{customer_id}/subscriptions.json</c>.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
