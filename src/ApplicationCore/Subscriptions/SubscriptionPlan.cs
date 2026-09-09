namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Provider-agnostic projection of a billing product.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>The billing-system product id. Numeric ids are not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; init; }

    /// <summary>Stable API handle of the plan (e.g. "eshop-pro"). This is what callers pass to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price, in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Whether the plan requires a stored payment method to subscribe.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
