using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed access layer over the Maxio Advanced Billing (Billing API) REST API.
/// Handles authentication (HTTP Basic, API key + "X"), JSON envelope unwrapping,
/// pagination, transport-level retry for idempotent reads, and error translation.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>
    /// Lists all non-archived products that belong to the given product family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer with the given reference (the eShopOnWeb user ID),
    /// or null when no customer has been provisioned yet.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. The reference must be unique; a 422 is thrown as
    /// <see cref="MaxioApiException"/> when another request created it first.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioNewCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to a customer (all states, newest first).
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer and product handle.
    /// A uniqueness token is attached so a transport-level retry can never
    /// create a duplicate subscription.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference, CancellationToken cancellationToken = default);
}
