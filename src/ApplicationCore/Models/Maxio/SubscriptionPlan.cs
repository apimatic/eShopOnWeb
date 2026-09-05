namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// A subscribable plan (a Maxio "Product" within the configured Product Family).
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }

    /// <summary>"month" or "day", as reported by Maxio.</summary>
    public string IntervalUnit { get; set; } = default!;

    /// <summary>True when Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
