using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeToPlanResult> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
