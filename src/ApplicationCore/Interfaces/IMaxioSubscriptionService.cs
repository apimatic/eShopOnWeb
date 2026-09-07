using System;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<MaxioSubscriptionPlan[]> GetPlansAsync();
    Task<MaxioCustomerSubscription> CreateSubscriptionAsync(string userId, string planHandle);
    Task<MaxioCustomerSubscription[]> GetCustomerSubscriptionsAsync(string userId);
}

public class MaxioSubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public int IntervalUnit { get; set; } // 1 = month, etc
    public string? IntervalUnitText { get; set; }
}

public class MaxioCustomerSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime? NextAssessmentAt { get; set; }
    public decimal? MrrAmount { get; set; }
    public string? BalanceInCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
