using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin typed client over the Maxio Advanced Billing (Billing API) REST API.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Returns the customer whose <c>reference</c> equals <paramref name="reference"/>,
    /// or null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Maxio customer.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerInput customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all non-archived products (plans) in the given product family (by handle).
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer to the given product handle.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? uniquenessToken = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription by its Maxio id.
    /// </summary>
    Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions that belong to a Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
