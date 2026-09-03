using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<CatalogPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<ShopperSubscription> SubscribeAsync(
        string userId,
        string email,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken);
}
