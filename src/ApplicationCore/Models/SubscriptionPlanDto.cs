namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A subscription plan (a Maxio product in the configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
