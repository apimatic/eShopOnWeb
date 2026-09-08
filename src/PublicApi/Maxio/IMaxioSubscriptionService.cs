using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(MaxioCustomerProfile profile, string productHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
