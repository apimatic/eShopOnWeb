using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken);
}
