using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<SubscriptionInfo?> GetPlanAsync(string handle, CancellationToken ct = default);
    Task<CustomerSubscriptionResult> EnsureCustomerAndSubscribeAsync(string email, string userId, string productHandle, CancellationToken ct = default);
    Task<SubscriptionInfo[]> ListMySubscriptionsAsync(string email, CancellationToken ct = default);
}
