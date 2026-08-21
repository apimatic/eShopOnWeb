using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);
    Task<BillingCustomer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken = default);
    Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);
    Task<SubscriptionDetails> CreateSubscriptionAsync(
        string customerReference,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default);
}
