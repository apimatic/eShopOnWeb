using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperSubscription>> ListSubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken);
}
