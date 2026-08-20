using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<ShopperSubscription> SubscribeAsync(string userName, string? productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken);
}
