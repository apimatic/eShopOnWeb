using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken ct = default);
}
