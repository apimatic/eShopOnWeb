using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// A subscription plan available for signup, as read from the Maxio catalog.
/// </summary>
public class SubscriptionPlanDto
{
    public int ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string BillingPeriod { get; set; } = string.Empty;
}
