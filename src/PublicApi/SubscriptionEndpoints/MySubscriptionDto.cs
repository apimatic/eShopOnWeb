using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public decimal PlanPrice { get; set; }
    public string? CurrentPeriodStartsAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
}
