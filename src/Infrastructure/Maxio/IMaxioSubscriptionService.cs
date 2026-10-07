using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<MaxioSubscription> SubscribeAsync(string userId, string userName, string? email, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
