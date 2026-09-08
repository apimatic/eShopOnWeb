namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan available for purchase, as reported by the billing system.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool IsDefault { get; set; }
}
