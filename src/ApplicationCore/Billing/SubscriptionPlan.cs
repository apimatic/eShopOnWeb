namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan a shopper can subscribe to, projected from a Maxio product.
/// Prices are expressed in integer cents (as Maxio returns them) plus a
/// pre-formatted display string for convenience.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable Maxio product API handle (e.g. "eshop-pro"). Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Numeric Maxio product id. Not stable across re-seeds; prefer <see cref="Handle"/>.</summary>
    public int ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>The billing interval count (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>The product's currency (e.g. "USD"), when reported by Maxio.</summary>
    public string? Currency { get; init; }
}
