using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Maxio Advanced Billing is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListShopperSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default);
}
