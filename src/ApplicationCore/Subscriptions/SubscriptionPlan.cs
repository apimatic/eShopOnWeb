namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Provider-agnostic projection of a billing
/// product/price so the rest of the application never depends on Maxio specifics.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (used when subscribing).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Display name of the plan.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price rendered in major units, culture-invariant (e.g. "299.00").</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Whether a stored payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Whether the plan is taxable.</summary>
    public bool Taxable { get; init; }

    /// <summary>Provider product id (numeric ids are not stable across re-seeds; prefer <see cref="Handle"/>).</summary>
    public int ProductId { get; init; }
}
