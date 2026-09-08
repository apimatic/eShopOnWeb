namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. Projected from a Maxio product;
/// Maxio remains the system of record. Prices are carried in minor units (cents)
/// exactly as Maxio reports them, plus a convenience decimal amount.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable product handle (e.g. "eshop-pro"). Handles are stable across re-seeds; numeric ids are not.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (cents), as reported by Maxio.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Convenience decimal amount (PriceInCents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code for display. Maxio's product model carries no currency, so this is the site default.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit wire value (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }
}
