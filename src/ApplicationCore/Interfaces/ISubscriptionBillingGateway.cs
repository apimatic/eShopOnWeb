using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> EnsureCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<BillingSubscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken);
}
