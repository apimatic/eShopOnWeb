using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionAppService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    /// <returns>The resulting subscription, and whether a new one was created (false when the
    /// user was already subscribed to this plan and the existing subscription was returned).</returns>
    Task<(SubscriptionDto Subscription, bool Created)> SubscribeCurrentUserAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetCurrentUserSubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
