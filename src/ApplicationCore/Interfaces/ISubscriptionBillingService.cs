using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<(CustomerSubscription Subscription, bool Created)> SubscribeAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        ShopperIdentity shopper,
        CancellationToken cancellationToken = default);
}
