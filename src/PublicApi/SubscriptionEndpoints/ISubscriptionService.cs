using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<Result<IReadOnlyList<SubscriptionPlanDto>>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<Result<SubscriptionCreateResult>> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SubscriptionDetailsDto>>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
