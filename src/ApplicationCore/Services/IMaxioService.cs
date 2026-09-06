using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionPlanDto
{
    public long ProductId { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceInCents { get; set; }
    public string State { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public interface IMaxioService
{
    Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync();
    Task<(long MaxioCustomerId, bool IsNew)> EnsureCustomerExistsAsync(string userId, string firstName, string lastName, string email);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, long maxioCustomerId, string productHandle);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, long maxioCustomerId);
}
