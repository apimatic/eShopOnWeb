using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioSubscriptionService
{
    Task<List<PlanDto>> GetAvailablePlansAsync();
    Task<SubscriptionResponseDto> CreateSubscriptionAsync(string userId, string productHandle);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionResponseDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAt { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
