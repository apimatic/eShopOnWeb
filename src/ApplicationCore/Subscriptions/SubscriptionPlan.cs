namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. Maps to a Maxio (Advanced Billing) Product
/// within the configured product family. Prices are recurring.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (e.g. "eshop-pro"). This is what callers subscribe by.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Maxio numeric product id. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; init; }

    /// <summary>Recurring price expressed in major units (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The numeric part of the billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>The interval unit ("month" or "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
