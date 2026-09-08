using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(BillingUser user, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(BillingUser user, CancellationToken ct = default);
}
