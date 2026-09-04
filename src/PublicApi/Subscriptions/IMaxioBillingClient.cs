using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
