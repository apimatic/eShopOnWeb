namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// Identified by <see cref="Handle"/>: numeric provider ids are not stable across catalog re-seeds,
/// so they are never exposed to (or accepted from) clients.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit, for clients that do their own formatting.</summary>
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Pre-formatted price, e.g. <c>$299.00</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing period, e.g. <c>every month</c>.</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when the provider requires a payment method before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
