using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level HTTP contract for the subset of the Maxio Advanced Billing API used by
/// eShopOnWeb, built against the OpenAPI spec in maxio-spec/. No business/idempotency
/// logic lives here - see <see cref="IMaxioSubscriptionService"/> for that.
/// </summary>
public interface IMaxioClient
{
    /// <summary>GET /product_families/handle:{handle}/products.json</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/lookup.json?reference={reference}. Returns null when no customer has that reference.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json using product_handle + customer_id</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
}
