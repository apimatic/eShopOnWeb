using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing subscription billing operations. Maxio is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(Shopper shopper, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(Shopper shopper, CancellationToken cancellationToken = default);
}
