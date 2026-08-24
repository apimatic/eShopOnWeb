using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    /// <summary>Lists all (non-archived) products on the site.</summary>
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the customer with the given reference, or null when none exists.</summary>
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string uniquenessToken, CancellationToken cancellationToken = default);
}
