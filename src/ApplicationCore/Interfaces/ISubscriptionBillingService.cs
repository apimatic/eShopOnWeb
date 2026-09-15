using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(SubscribeToPlan request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
