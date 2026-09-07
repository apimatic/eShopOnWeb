using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct);
    Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken ct);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct);
}
