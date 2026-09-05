namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "product"), projected from the site's catalog.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = default!;
    public string ProductFamilyHandle { get; set; } = default!;
}
