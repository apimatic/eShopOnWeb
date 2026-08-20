using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken);

    Task<Result<ShopperSubscription>> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ShopperSubscription>>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken);
}
