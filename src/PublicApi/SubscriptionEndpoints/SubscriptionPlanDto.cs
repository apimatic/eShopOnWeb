namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for purchase.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string IntervalUnit { get; set; } = "month";
    public int Interval { get; set; }
}
