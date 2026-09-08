using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

/// <summary>
/// Client for the subset of the Maxio Advanced Billing (Billing API) surface
/// used by the subscription feature. All calls are authenticated with the
/// site API key via HTTP Basic authentication.
/// </summary>
public interface IMaxioBillingClient
{
    /// <summary>
    /// Reads a product family by handle. Returns null when it does not exist.
    /// </summary>
    Task<MaxioProductFamily?> GetProductFamilyByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the (non-archived) products belonging to a product family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a product by its API handle. Returns null when it does not exist.
    /// </summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a customer by the reference value supplied by this application.
    /// Returns null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a customer. The reference value must be unique in the site;
    /// if it already exists the API returns 422 and this method throws.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all subscriptions that belong to a customer (all pages).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a subscription for an existing customer on a product handle.
    /// The products in the demo catalog do not require a payment method.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken);
}
