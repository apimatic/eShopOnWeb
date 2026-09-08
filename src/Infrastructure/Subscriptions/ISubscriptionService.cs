using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDetails> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
