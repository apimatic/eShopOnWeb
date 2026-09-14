namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscribable plan (Maxio "product"), read live from the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresCreditCard { get; set; }
}
