using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(
        ShopperProfile shopper,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        ShopperProfile shopper,
        CancellationToken cancellationToken);
}
