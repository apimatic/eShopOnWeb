using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);

    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionDto>> ListUserSubscriptionsAsync(string userName, CancellationToken ct = default);
}
