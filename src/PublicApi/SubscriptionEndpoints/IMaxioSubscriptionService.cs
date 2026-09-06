using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<int?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<int?> GetMaxioCustomerIdAsync(string userId);
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, int customerId, string planHandle);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}
