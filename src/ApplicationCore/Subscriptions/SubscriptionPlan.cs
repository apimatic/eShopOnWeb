namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in (a Maxio "product" within the configured product family).
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier used when subscribing (e.g. "eshop-pro"). Never changes across re-seeds.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval length, paired with <see cref="IntervalUnit"/> (e.g. 1 "month").</summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;
}
