using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) surfaced for browsing.
/// </summary>
public class SubscriptionPlanDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
