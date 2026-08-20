using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email,
        string uniquenessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle,
        string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken);
}
