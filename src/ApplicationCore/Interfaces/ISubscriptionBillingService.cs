using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(
        string buyerId,
        string email,
        string userName,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default);
}
