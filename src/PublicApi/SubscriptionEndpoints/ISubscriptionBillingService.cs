using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);
