using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionSummary> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
