namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A recurring subscription plan (a product in the billing system's product family).
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
