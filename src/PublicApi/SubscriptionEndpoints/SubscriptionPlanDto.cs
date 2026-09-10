namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan a shopper can enroll in (a Maxio product in the configured family).</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle used to subscribe (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents (Maxio's native unit).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Numeric billing interval, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
