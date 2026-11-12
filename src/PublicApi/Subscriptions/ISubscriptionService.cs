using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public record SubscribeResult(SubscriptionDto Subscription, bool Created);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal caller, string? planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal caller, CancellationToken ct);
}
