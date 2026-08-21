using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default);
}
