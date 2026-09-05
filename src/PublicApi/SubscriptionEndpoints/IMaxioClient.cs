using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken);
}
