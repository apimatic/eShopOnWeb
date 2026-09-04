using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, string uniquenessToken, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken);

    Task<MaxioSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(CancellationToken cancellationToken);
}
