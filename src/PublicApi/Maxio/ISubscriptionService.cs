using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<PlansResult> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
