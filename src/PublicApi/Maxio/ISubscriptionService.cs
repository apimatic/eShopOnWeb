using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<PlanList> ListPlansAsync(CancellationToken ct);
    /// <returns>The subscription, and whether it was created by this call (false = it already existed).</returns>
    Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(string userName, string? planHandle, CancellationToken ct);
    Task<(List<SubscriptionDto> Items, bool Truncated)> ListMySubscriptionsAsync(string userName, CancellationToken ct);
}
