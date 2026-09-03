using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<ShopperSubscription> SubscribeAsync(string customerReference, string email, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken);
}
