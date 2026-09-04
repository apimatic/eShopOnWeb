using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
