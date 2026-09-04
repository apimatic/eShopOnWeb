using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        string planHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
