using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionDto?> SubscribeAsync(string? planHandle);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync();
}
