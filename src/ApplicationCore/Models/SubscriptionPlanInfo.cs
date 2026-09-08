namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan available for purchase, sourced from the billing system of record.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
