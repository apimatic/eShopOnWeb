namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in — a Maxio product within the configured product
/// family, projected into the domain (money already normalised from cents to a decimal amount).
/// </summary>
public sealed record SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }

    /// <summary>Recurring price in the plan's currency (normalised from Maxio's integer cents).</summary>
    public decimal Price { get; init; }

    /// <summary>Raw price in cents as reported by Maxio, preserved for callers that need exact units.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Interval unit as reported by Maxio (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; init; }
}
