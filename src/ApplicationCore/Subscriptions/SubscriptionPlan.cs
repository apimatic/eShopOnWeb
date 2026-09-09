namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Backed by a Maxio product within the
/// configured product family. Identified by its stable <see cref="Handle"/> (numeric IDs
/// are reassigned by Maxio on re-seed and must not be relied upon).
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-readable price, e.g. <c>$299.00</c>.</summary>
    public required string FormattedPrice { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>The numeric billing interval, e.g. <c>1</c> for every month.</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }
}
