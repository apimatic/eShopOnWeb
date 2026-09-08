using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin async client over the subset of the Maxio Billing API used for subscriptions.
/// </summary>
public interface IMaxioApiClient
{
    /// <summary>Lists every product (plan) in the site, across all pages.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks a customer up by their unique application reference; null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a customer. Reference values are unique in Maxio.</summary>
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerPayload customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subscription for an existing customer to a product handle. Pass a
    /// paymentCollectionMethod (e.g. "remittance") to override the site default.
    /// </summary>
    Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle, int customerId, string uniquenessToken,
        string? paymentCollectionMethod = null, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions owned by a customer.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
}
