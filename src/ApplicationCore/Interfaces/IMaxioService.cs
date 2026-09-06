using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<int> GetOrCreateMaxioCustomerAsync(string userId, string email);
    Task<MaxioSubscriptionDto> CreateSubscriptionAsync(int maxioCustomerId, string productHandle);
    Task<List<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(int maxioCustomerId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PricingScheme { get; set; } = string.Empty;
    public int? TrialDays { get; set; }
}

public class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
}
