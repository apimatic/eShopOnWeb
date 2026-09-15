using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default);

    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);
}
