using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Minimal typed client over the Maxio Advanced Billing REST API.
/// Auth is HTTP Basic with the API key as username and "X" as password.
/// </summary>
public interface IMaxioClient
{
    /// <summary>Lists products belonging to a product family (path accepts "handle:{handle}").</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns the customer with the given reference, or null when none exists (404).</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    /// <summary>Returns the subscription with the given reference, or null when none exists (404).</summary>
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes subscription, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
