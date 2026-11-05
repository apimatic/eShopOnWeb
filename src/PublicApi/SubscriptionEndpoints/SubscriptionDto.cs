using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
