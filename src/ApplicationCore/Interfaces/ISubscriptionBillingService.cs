using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken);
}
