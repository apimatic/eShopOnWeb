using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlanListResult> ListPlansAsync();
    Task<SubscriptionResult> SubscribeAsync(string userId, string userEmail, string productHandle);
    Task<UserSubscriptionListResult> GetMySubscriptionsAsync(string userId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
}

public class SubscriptionPlanListResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserSubscriptionListResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
