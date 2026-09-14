namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "product") within the site's subscription product family.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
