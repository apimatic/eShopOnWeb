using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(
        string userName,
        string planHandle,
        string firstName,
        string lastName,
        string? email,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentSubscription>> ListSubscriptionsForUserAsync(
        string userName,
        CancellationToken cancellationToken);
}
