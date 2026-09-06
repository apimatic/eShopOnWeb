using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(string userId, string firstName, string lastName, string email, string planHandle);
    Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int maxioCustomerId);
    Task<int?> GetOrCreateMaxioCustomerAsync(string userId, string firstName, string lastName, string email);
}
