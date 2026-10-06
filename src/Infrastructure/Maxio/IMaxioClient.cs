using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed client for the small slice of the Maxio Advanced Billing API this
/// integration needs. Built strictly against the OpenAPI spec in maxio-spec/.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// GET /customers/lookup.json?reference={reference} — exact lookup by the
    /// app's own customer reference. Returns null when the reference is unknown (404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>POST /customers.json — creates a billing customer.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /product_families/handle:{handle}/products.json — lists the products
    /// (subscription plans) in a product family, addressed by handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>POST /subscriptions.json — creates a subscription.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /customers/{customer_id}/subscriptions.json — lists all subscriptions
    /// that belong to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}