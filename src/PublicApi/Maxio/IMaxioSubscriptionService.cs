using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(string userReference, string userEmail, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken);
}
