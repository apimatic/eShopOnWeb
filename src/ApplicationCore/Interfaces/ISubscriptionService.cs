using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingSubscription> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListForShopperAsync(ShopperIdentity shopper, CancellationToken cancellationToken);
}
