namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. In Maxio Advanced Billing a plan maps to a Product
/// within a Product Family; the stable <see cref="Handle"/> is used to subscribe.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Maxio product id. Numeric ids are not stable across re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; init; }

    /// <summary>Stable API handle used to identify the plan when subscribing (e.g. "eshop-pro").</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numeric billing interval, e.g. 1.</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;
}
