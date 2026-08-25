namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A subscription plan (a product in the billing system's product family) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlan
{
    public long ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
