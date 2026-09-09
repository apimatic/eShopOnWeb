using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing (Billing API) REST endpoints used by this app.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists the (non-archived) products in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a customer by the reference value assigned by this app. Returns null when no match exists.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer on the given product.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
