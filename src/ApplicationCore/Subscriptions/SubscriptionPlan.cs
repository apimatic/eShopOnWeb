namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Projected from a Maxio Product within the configured
/// product family. The <see cref="Handle"/> is the stable identifier used when subscribing;
/// numeric ids are intentionally not surfaced because Maxio reassigns them on re-seed.
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in major currency units (e.g. dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval unit as reported by Maxio (e.g. <c>month</c>, <c>day</c>).</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Number of interval units between renewals (e.g. 1 for monthly).</summary>
    public int IntervalCount { get; init; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
