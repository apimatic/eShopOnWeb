using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken);
    Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<SubscriptionDetails> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken);
    Task<SubscriptionDetails> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
