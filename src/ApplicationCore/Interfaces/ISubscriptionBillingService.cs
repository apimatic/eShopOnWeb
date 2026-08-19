using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingSubscription> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
