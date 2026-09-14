using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(MaxioShopper shopper, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetailsDto>> GetSubscriptionsAsync(MaxioShopper shopper, CancellationToken cancellationToken);
}
