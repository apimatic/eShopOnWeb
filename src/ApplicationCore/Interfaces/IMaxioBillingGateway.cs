using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the Maxio Advanced Billing HTTP API used by subscription enrollment.
/// </summary>
public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionPlan?> GetPlanByHandleAsync(string productHandle, CancellationToken cancellationToken);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, string uniquenessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);

    Task<ShopperSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken);

    Task<ShopperSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
}
