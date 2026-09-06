using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default);
    Task<int> EnsureCustomerExistsAsync(string userId, string firstName, string lastName, string email, CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);
    Task<SubscriptionDto?> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
