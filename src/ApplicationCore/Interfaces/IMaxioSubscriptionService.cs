using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string userEmail, string userFirstName, string userLastName, string planHandle);
    Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userId);
}

public class MaxioSubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
}
