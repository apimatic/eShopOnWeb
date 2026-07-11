using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> ListAvailablePlansAsync();
    Task<SubscriptionDto> SubscribeAsync(string userId, int productId);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId);
    Task RecordUsageAsync(string userId, int subscriptionId, int componentId, decimal quantity, string? memo = null);
    Task<UsageDto> GetUsageAsync(string userId, int subscriptionId, int componentId);
    Task<PlanChangePreviewDto> PreviewPlanChangeAsync(string userId, int subscriptionId, int newProductId);
    Task<SubscriptionDto> ChangePlanAsync(string userId, int subscriptionId, int newProductId);
    Task<SubscriptionDto> PauseSubscriptionAsync(string userId, int subscriptionId);
    Task<SubscriptionDto> ResumeSubscriptionAsync(string userId, int subscriptionId);
    Task<SubscriptionDto> CancelSubscriptionAsync(string userId, int subscriptionId, bool atEndOfPeriod = false);
    Task<SubscriptionDto> ReactivateSubscriptionAsync(string userId, int subscriptionId);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public SubscriptionState State { get; set; }
    public decimal? CurrentPeriodEndsAt { get; set; }
    public decimal? NextBillingAt { get; set; }
}

public class UsageDto
{
    public decimal CurrentUsage { get; set; }
    public decimal UnitPrice { get; set; }
}

public class PlanChangePreviewDto
{
    public decimal HighestCharge { get; set; }
    public decimal LowestCharge { get; set; }
    public decimal ProrationAdjustment { get; set; }
}
