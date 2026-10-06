namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as offered by the billing system.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// API handle of the plan; pass it as planHandle when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool IsDefault { get; set; }
}