namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. Provider-agnostic projection of a billing
/// product/plan; the stable identifier is <see cref="Handle"/> (numeric ids are not stable).
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (e.g. <c>eshop-pro</c>).</summary>
    public required string Handle { get; init; }

    public string? Name { get; init; }

    /// <summary>Recurring price in the plan currency's minor units (cents).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, when a price is known.</summary>
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>Billing interval length (e.g. 1) paired with <see cref="IntervalUnit"/>.</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
