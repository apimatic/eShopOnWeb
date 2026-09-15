using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Maxio Advanced Billing is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
