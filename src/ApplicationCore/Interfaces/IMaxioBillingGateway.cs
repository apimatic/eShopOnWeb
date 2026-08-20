using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> EnsureCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken);
    Task<SubscriptionDetails> EnsureSubscriptionAsync(
        string productHandle,
        long customerId,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken);
}
