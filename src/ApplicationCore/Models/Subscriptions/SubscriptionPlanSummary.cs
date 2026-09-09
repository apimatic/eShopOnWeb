namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A subscription plan (a Maxio product) that shoppers can subscribe to.
/// </summary>
public class SubscriptionPlanSummary
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool HasTrial { get; set; }
    public bool RequireCreditCard { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
