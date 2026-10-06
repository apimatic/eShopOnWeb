using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Client for the Maxio Advanced Billing REST API.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists the non-archived products (plans) in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by its application reference, or returns null when no such customer exists.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. The reference value must be unique within the site.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions that belong to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer (identified by reference) on the given product handle.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken cancellationToken = default);
}
