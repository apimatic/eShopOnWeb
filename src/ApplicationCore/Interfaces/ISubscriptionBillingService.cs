using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(ShopperBillingProfile shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string shopperId, CancellationToken cancellationToken = default);
}
