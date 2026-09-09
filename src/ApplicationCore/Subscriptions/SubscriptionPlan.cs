namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Maps to a product in the configured billing product family.
/// Handles are stable identifiers; numeric ids are not relied upon.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price, in cents (the billing system's canonical representation).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval count, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }
}
