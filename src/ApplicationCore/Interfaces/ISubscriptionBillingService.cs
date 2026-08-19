using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<ShopperSubscription> SubscribeAsync(string userName, string email, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
