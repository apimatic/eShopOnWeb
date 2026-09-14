namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribe-able plan surfaced by the PublicApi. Prices are in major currency
/// units (e.g. 299.00), not cents.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio plan/product handle (stable; safe to echo back when subscribing).</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring charge in <see cref="Currency"/> per billing interval (null when Maxio reports none).</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO currency code the price is quoted in (e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>Length of the billing interval (1 = every interval).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when this is the catalog's default subscription plan.</summary>
    public bool IsDefault { get; set; }

    /// <summary>True when subscribing to this plan requires a stored payment method.</summary>
    public bool PaymentRequired { get; set; }
}
