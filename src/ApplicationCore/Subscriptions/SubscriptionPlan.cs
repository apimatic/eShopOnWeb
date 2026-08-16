namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Maps to a Maxio product within the configured
/// product family. <see cref="Handle"/> is stable across re-seeds; numeric ids are not.
/// </summary>
public sealed class SubscriptionPlan
{
    public int ProductId { get; init; }

    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price of the plan in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public required string FormattedPrice { get; init; }

    /// <summary>The numerical billing interval, e.g. 1.</summary>
    public int Interval { get; init; }

    /// <summary>The interval unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }
}
