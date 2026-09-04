using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDto> SubscribeAsync(string userName, string? requestedPlanHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
