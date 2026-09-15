using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(
        string buyerId,
        string email,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken);
}
