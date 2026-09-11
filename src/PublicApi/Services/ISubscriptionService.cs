using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}
