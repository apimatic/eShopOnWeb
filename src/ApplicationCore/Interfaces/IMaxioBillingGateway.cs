using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(BillingUser user, string customerReference, CancellationToken cancellationToken);
    Task<SubscriptionDetails?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken);
    Task<SubscriptionDetails> CreateSubscriptionAsync(string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}
