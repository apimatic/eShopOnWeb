using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionEnrollment> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
