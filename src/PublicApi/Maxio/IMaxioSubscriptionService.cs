using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string username, CancellationToken ct);
}
