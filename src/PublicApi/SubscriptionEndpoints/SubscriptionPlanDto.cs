namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public int Interval { get; set; }
}
