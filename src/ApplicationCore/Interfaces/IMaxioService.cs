using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<List<SubscriptionPlan>> GetAvailablePlansAsync();
    Task<Subscription> CreateSubscriptionAsync(string userId, string userEmail, string planHandle);
    Task<List<Subscription>> GetUserSubscriptionsAsync(string userId);
    Task<bool> EnsureCustomerExistsAsync(string userId, string userEmail);
}
