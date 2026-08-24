namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan (Maxio product) a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
