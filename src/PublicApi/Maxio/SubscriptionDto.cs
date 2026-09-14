using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
