using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}
