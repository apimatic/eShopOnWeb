using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// HTTP client for the Maxio Advanced Billing REST API. The Maxio OpenAPI specification in
/// maxio-spec/ is the authoritative contract every call is built against.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Lists the (non-archived) plans/products that belong to a product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(string familyHandle, CancellationToken ct = default);

    /// <summary>Looks up a customer by its app-side reference. Returns null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>Creates a customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken ct = default);

    /// <summary>Creates a subscription for an existing customer (identified by reference).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken ct = default);

    /// <summary>Lists all subscriptions that belong to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken ct = default);
}
