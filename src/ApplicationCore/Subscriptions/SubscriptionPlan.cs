namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can enroll in, projected from a
/// Maxio Advanced Billing product. Contains only presentation-safe data — no
/// SDK types leak into ApplicationCore.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable product handle (used to subscribe). Handles are stable; numeric ids are not.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price for one billing period, in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Convenience view of <see cref="PriceInCents"/> expressed in the major currency unit.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of interval units per billing period (e.g. 1 for "every 1 month").</summary>
    public int Interval { get; set; }

    /// <summary>Interval unit wire value, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }
}
