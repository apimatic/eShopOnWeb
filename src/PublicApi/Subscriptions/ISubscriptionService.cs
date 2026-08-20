using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record ShopperIdentity(string UserId, string UserName, string Email);

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ShopperIdentity shopper, CancellationToken cancellationToken);
}
