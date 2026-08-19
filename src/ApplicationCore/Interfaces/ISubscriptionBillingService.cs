using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> SubscribeAsync(SubscribeShopperRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string shopperUserId, CancellationToken cancellationToken = default);
}
