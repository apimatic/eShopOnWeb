namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. Maps to a Maxio Advanced Billing
/// "product" that belongs to the configured product family.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, catalog-independent identifier used when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the site's currency, expressed in integer cents.</summary>
    public int PriceInCents { get; set; }

    /// <summary>Human-friendly price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Whether a payment method must be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
