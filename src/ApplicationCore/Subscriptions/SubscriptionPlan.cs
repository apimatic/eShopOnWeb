namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. This is a provider-agnostic
/// projection of a billing "product" (in Maxio terms) within the configured product family.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier for the plan (Maxio product handle). Numeric ids are not stable.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price expressed in the major currency unit (e.g. dollars), derived from the provider's cents value.</summary>
    public decimal Price { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period (e.g. 1).</summary>
    public int IntervalCount { get; init; }

    /// <summary>Billing period unit, e.g. "month" or "day".</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Whether the provider requires a stored payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
