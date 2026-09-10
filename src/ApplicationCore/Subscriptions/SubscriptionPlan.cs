namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can subscribe to. Projected from a Maxio product
/// within the configured product family. Prices are carried in cents (the billing system's unit)
/// to avoid rounding drift; the API layer formats them for display.
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }

    /// <summary>Number of interval units per billing period (e.g. 1 for monthly).</summary>
    public int IntervalCount { get; init; }

    /// <summary>The billing interval unit as reported by Maxio (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; init; }
}
