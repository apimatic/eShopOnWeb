namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription plan a shopper can enrol in — an eShopOnWeb-facing projection of a Maxio product.
/// </summary>
public class SubscriptionPlan
{
    public int? ProductId { get; init; }

    /// <summary>Stable product handle (e.g. <c>eshop-pro</c>) — the identifier used to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Raw price in the smallest currency unit (cents), as returned by Maxio.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Decimal price derived from <see cref="PriceInCents"/> (cents / 100).</summary>
    public decimal Price { get; init; }

    /// <summary>ISO currency code the price is expressed in.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit wire value (e.g. <c>month</c>, <c>day</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;
}
