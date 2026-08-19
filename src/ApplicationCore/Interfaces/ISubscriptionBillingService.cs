using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(BillingShopper shopper, CancellationToken cancellationToken);
}
