using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<List<PlanModel>> GetAvailablePlansAsync(CancellationToken ct);
    Task<SubscriptionModel?> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken ct);
    Task<List<SubscriptionModel>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}
