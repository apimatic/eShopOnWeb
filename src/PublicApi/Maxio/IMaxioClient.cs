using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin async wrapper over the Maxio Advanced Billing REST API.
/// Lookup methods return null when the resource does not exist (HTTP 404).
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists the products (plans) belonging to a product family, addressed by the family handle.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a single product by its API handle; null when not found.</summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds a customer by the application-owned reference value; null when not found.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. Maxio enforces uniqueness of the reference value.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest customer, CancellationToken cancellationToken = default);

    /// <summary>Finds a subscription by the application-owned reference value; null when not found.</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a subscription for an existing customer (by reference) to a product (by handle).</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest subscription, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions belonging to a Maxio customer id.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
