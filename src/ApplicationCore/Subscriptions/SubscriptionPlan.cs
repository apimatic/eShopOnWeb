namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribe-able plan, projected from a Maxio product. Prices are carried in integer
/// cents (as Maxio reports them) plus a display-friendly decimal amount.
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required int PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00), derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of interval units per billing period (e.g. 1).</summary>
    public required int Interval { get; init; }

    /// <summary>Interval unit as reported by Maxio (e.g. "month", "day").</summary>
    public required string IntervalUnit { get; init; }

    public required string ProductFamilyHandle { get; init; }
}
