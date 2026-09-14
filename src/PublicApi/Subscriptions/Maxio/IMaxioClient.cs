using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDetails customer, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDetails subscription, CancellationToken cancellationToken);
    Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
