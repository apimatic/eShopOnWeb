namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio Advanced Billing
/// product within the configured product family. Prices are held in the smallest currency
/// unit (cents) exactly as the billing system reports them, to avoid rounding drift.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, catalog-agnostic identifier for the plan (Maxio product handle).</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public int PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit the plan bills on (e.g. "month", "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Recurring price expressed as a decimal amount in the plan currency.</summary>
    public decimal Price => PriceInCents / 100m;
}
