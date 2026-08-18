using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Billing operations backed by Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListShopperSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default);
}
