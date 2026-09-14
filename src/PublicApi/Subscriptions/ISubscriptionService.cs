using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string userName, CancellationToken cancellationToken);
}
