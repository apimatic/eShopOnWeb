using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class StubSubscriptionService : ISubscriptionService
{
    public Task<IEnumerable<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        return Task.FromResult(Enumerable.Empty<SubscriptionPlanDto>());
    }

    public Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle)
    {
        throw new NotImplementedException("Maxio subscription service is not configured.");
    }

    public Task<IEnumerable<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        return Task.FromResult(Enumerable.Empty<SubscriptionDto>());
    }
}
