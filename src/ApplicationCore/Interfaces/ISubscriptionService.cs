using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;

public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken ct = default);
    Task<MaxioSubscription> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}
