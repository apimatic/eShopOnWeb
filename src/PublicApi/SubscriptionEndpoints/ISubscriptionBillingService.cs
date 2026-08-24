using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<CreateSubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
