using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string shopperUserId, CancellationToken cancellationToken);
}
