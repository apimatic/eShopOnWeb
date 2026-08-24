using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the Maxio Advanced Billing API.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists the products (plans) belonging to the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by its unique reference. Returns null when no customer exists (404).
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new customer. The reference must be unique per customer.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer (identified by reference) to a product (identified by handle).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string? subscriptionReference = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
