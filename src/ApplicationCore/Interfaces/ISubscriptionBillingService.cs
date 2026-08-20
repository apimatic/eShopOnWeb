using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<ShopperSubscription> SubscribeAsync(
        string buyerId,
        string email,
        string? displayName,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);
}
