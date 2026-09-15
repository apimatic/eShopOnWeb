using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}
