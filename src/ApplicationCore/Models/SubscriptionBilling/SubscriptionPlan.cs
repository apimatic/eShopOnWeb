namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// A subscription plan offered to shoppers, backed by a Maxio product.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
