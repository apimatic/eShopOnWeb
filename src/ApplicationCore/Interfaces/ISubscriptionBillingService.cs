using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanPage> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(ShopperAccount shopper, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperSubscription>> ListMineAsync(ShopperAccount shopper, CancellationToken cancellationToken);
}
