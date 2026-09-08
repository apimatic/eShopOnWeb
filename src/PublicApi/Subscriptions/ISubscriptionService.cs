using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string appUserId, string email, string planHandle, string? firstName, string? lastName, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string appUserId, string email, CancellationToken cancellationToken);
}
