using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscriptionDto> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
