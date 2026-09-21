namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. Maps to a Maxio product (within the configured
/// product family). Amounts are in cents; the caller formats for display.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable Maxio product API handle (used to subscribe). Never a numeric id.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in cents for the product's default price point.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit wire value (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>The product family handle this plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
