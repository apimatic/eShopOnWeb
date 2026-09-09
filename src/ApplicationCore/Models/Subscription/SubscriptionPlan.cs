namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A subscribable plan offered by the external billing system (Maxio Advanced Billing product).
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequiresPaymentMethod { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
