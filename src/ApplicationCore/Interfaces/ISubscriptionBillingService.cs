using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-facing subscription billing. Maxio remains the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(
        SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
