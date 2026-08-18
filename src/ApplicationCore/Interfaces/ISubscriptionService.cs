using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<ShopperSubscription> SubscribeAsync(SubscribeShopperRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
