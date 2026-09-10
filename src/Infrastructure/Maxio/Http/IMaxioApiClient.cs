using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// A thin, one-to-one client over the Maxio Advanced Billing REST endpoints that eShopOnWeb uses.
/// Each method maps to a single operation in the OpenAPI contract and returns/accepts wire models.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>GET <c>/product_families/handle:{familyHandle}/products.json</c> — the plans in a product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET <c>/customers/lookup.json?reference=...</c> — the customer with the given reference, or null if none.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/customers.json</c> — creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>POST <c>/subscriptions.json</c> — creates a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>GET <c>/customers/{customerId}/subscriptions.json</c> — all subscriptions for a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
