using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);
    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);
    Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}
