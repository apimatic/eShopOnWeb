using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Contract for the subset of the Maxio Advanced Billing API used by eShopOnWeb subscriptions.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists all non-archived products (plans) visible to the site.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a product by its stable handle. Returns null when no product matches.
    /// </summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a customer by its application-side reference (the eShopOnWeb user id).
    /// Returns null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a Maxio customer.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all subscriptions that belong to a Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a subscription for a customer to a product, without requiring a payment
    /// method (remittance / invoice collection).
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken);
}
