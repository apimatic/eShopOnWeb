using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct);

    Task<SubscribeResult> SubscribeAsync(SubscriberProfile subscriber, string planHandle, CancellationToken ct);

    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string reference, CancellationToken ct);
}
