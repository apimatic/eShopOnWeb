using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Contract for the Maxio Advanced Billing REST API. See the Maxio Billing API
/// documentation for endpoint details.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists all products (plans) in a product family, by family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> GetProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single product by its API handle. Returns null when it does not exist.
    /// </summary>
    Task<MaxioProduct?> GetProductByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a customer by the app-side reference value. Returns null when no customer matches.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer. <c>MaxioReferenceTakenException</c> is thrown when the reference is already used.
    /// </summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer and a product handle.
    /// Only valid for products that do not require a payment method.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription. Returns null when it does not exist.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions that belong to a customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}