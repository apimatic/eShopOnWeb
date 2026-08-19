using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to Maxio Advanced Billing, the system of record for subscription billing.
/// </summary>
public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(ShopperIdentity shopper, string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        string subscriptionReference,
        CancellationToken cancellationToken = default);
}
