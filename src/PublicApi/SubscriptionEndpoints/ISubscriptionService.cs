using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId);
}
