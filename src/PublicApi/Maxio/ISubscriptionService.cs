using System;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<SubscriptionPlan[]> GetAvailablePlansAsync();
    Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string planHandle);
    Task<SubscriptionDto[]> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Interval { get; set; } = string.Empty;
    public int IntervalCount { get; set; }
    public long ProductId { get; set; }
}

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Interval { get; set; } = string.Empty;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
