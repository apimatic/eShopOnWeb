using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken);
}
