namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A billing plan a shopper can subscribe to. Modeled from a Maxio product that
/// belongs to the configured product family.
/// </summary>
public record SubscriptionPlan
{
    public SubscriptionPlan(
        int productId,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Maxio product id. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public int ProductId { get; }

    /// <summary>Stable API handle of the plan (e.g. "eshop-pro").</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; }

    /// <summary>Number of interval units per billing period (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Interval unit for the billing period ("day" or "month").</summary>
    public string IntervalUnit { get; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice => $"${PriceInCents / 100m:0.00}";
}
