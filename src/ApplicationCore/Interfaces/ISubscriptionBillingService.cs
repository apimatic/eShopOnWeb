using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<ShopperSubscription> SubscribeAsync(SubscribeShopperRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
