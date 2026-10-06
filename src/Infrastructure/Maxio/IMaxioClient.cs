using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin transport layer over the Maxio Advanced Billing (Billing API) REST
/// endpoints used by the subscription integration.
/// </summary>
public interface IMaxioClient
{
    Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists active products (plans) that belong to the given product family handle.
    /// </summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsInFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer with the given application reference, or null when none exists.
    /// </summary>
    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a product. Uses invoice (remittance) collection so
    /// enrollment does not require a captured payment method. The subscription
    /// reference must be unique per site; Maxio rejects duplicates, which the
    /// service uses for cross-instance idempotency.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscription with the given reference, or null when none exists.
    /// </summary>
    Task<MaxioSubscription?> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a subscription immediately (used for cleanup and self-service offboarding).
    /// </summary>
    Task<MaxioSubscription> CancelSubscriptionAsync(int subscriptionId, string message, CancellationToken cancellationToken = default);
}