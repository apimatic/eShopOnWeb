using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionSummary> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default);
}
