using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
