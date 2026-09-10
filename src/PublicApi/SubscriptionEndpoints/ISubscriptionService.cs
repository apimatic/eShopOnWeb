using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API-layer orchestration for subscription billing: resolves the authenticated caller's
/// identity from the JWT into a billing subscriber, delegates to the Maxio integration, and
/// projects domain results into transport DTOs.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscriptionDto> SubscribeAsync(ClaimsPrincipal user, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
