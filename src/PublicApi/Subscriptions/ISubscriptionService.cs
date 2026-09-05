using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
