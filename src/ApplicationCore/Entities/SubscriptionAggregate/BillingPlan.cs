using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as published by the billing provider.
/// <see cref="Price"/> is expressed in whole currency units (e.g. dollars), never minor units.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id,
        string handle,
        string name,
        decimal price,
        int interval,
        string intervalUnit)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.Negative(price, nameof(price));
        Guard.Against.NullOrEmpty(intervalUnit, nameof(intervalUnit));

        Id = id;
        Handle = handle;
        Name = name;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Provider-assigned identifier. Not stable across a sandbox re-seed — prefer <see cref="Handle"/>.</summary>
    public int Id { get; }

    /// <summary>The durable, human-authored identifier for this plan.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; init; }

    /// <summary>Recurring price in whole currency units (dollars), not minor units (cents).</summary>
    public decimal Price { get; }

    /// <summary>The number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; }

    /// <summary>The billing period unit reported by the provider (e.g. <c>month</c>, <c>day</c>).</summary>
    public string IntervalUnit { get; }

    public string? ProductFamilyHandle { get; init; }

    public bool RequiresPaymentMethod { get; init; }

    public bool IsArchived { get; init; }
}
