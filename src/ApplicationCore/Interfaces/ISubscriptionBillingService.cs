using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken);
}
