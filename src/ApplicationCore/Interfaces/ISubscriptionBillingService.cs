using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<ShopperSubscription> SubscribeAsync(
        BillingBuyer buyer,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string buyerReference,
        CancellationToken cancellationToken = default);
}
