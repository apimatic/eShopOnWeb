using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<PlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<CustomerDto?> GetOrCreateCustomerAsync(string userId, string email, string? firstName = null, string? lastName = null, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle, CancellationToken ct = default);
}
