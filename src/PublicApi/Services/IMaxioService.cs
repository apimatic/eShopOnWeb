using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> ListSubscriptionPlansAsync(CancellationToken ct = default);
    Task<(int?, string? Reference)> GetOrCreateMaxioCustomerAsync(string userEmail, string userId, CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
}
