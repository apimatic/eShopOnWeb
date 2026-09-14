namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. Projected from a Maxio Product that belongs
/// to the configured product family. Prices are carried in cents (Maxio's native unit) with a
/// convenience decimal accessor.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (Maxio product handle) — used to subscribe.</summary>
    public required string Handle { get; init; }

    /// <summary>Human-friendly plan name.</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in cents.</summary>
    public int PriceInCents { get; init; }

    /// <summary>Recurring price expressed as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public required string ProductFamilyHandle { get; init; }

    /// <summary>Name of the plan's default price point, when present.</summary>
    public string? PricePointName { get; init; }
}
