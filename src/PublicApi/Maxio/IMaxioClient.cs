using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed client for the subset of the Maxio Advanced Billing API this integration uses.
/// </summary>
public interface IMaxioClient
{
    /// <summary>List the products (plans) in the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Find a customer by its external reference. Returns null when no such customer exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Create a customer. Throws <see cref="MaxioApiException"/> (422) when the reference already exists.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    /// <summary>Create a subscription for an existing customer to a product identified by its handle.</summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, CancellationToken cancellationToken = default);

    /// <summary>List all subscriptions belonging to a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
