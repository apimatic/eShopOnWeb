using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanDto[]> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct = default);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}
