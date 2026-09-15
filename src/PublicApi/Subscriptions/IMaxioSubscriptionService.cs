using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionSummary> SubscribeAsync(string userIdentity, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string userIdentity, CancellationToken cancellationToken);
}
