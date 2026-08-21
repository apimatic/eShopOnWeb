using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(
        BillingCustomerIdentity identity,
        string reference,
        CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(
        long customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken);
}
