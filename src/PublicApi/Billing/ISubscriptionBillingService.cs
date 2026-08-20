using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<UserSubscription> SubscribeAsync(
        string productHandle,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(CancellationToken cancellationToken);
}
