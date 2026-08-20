using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(ApplicationUser user, string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(ApplicationUser user,
        CancellationToken cancellationToken);
}
