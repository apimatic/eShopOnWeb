namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan (Maxio "Product") a shopper can subscribe to.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
