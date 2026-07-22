using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to, normalized from the billing provider's product catalog.
/// <see cref="Price"/> is always expressed in whole currency units (dollars), never minor units.
/// </summary>
public sealed record SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        decimal price,
        int interval,
        string intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw new ArgumentException("A plan handle is required.", nameof(handle));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A plan name is required.", nameof(name));

        Id = id;
        Handle = handle;
        Name = name;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Provider-assigned numeric identifier. Not stable across sandbox re-seeds.</summary>
    public int Id { get; init; }

    /// <summary>The durable identifier for the plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; init; }

    public string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price per billing period, in dollars.</summary>
    public decimal Price { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit as reported by the provider (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public bool IsArchived { get; init; }

    /// <summary>
    /// True when the provider demands a payment method before a subscription can be created. The demo
    /// plans are seeded with this off so UC1 never needs card capture.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Human-readable billing cadence, e.g. "month" or "3 months".</summary>
    public string BillingPeriodDescription =>
        Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s";
}
