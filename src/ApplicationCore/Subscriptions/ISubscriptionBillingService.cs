using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDetails> SubscribeAsync(
        SubscriptionShopper shopper,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken);
}
