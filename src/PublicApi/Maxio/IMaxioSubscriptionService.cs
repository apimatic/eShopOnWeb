using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<MaxioPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken);

    Task<MaxioSubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken);
}