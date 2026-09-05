using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Subscriptions;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlan>> GetAvailablePlansAsync();
    Task<UserSubscription> CreateSubscriptionAsync(string userId, string userEmail, string? firstName, string? lastName, string planHandle);
    Task<List<UserSubscription>> GetUserSubscriptionsAsync(string userId);
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string handle);
}
