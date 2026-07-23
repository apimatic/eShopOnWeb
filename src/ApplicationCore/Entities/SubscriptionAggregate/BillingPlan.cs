using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as reported by the billing provider.
/// </summary>
/// <remarks>
/// Money is carried in <b>minor units (cents)</b> exactly as the provider reports it; callers that
/// need a currency amount use <see cref="Price"/> rather than dividing themselves.
/// </remarks>
public class BillingPlan
{
    public BillingPlan(int id,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        bool isArchived)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        IsArchived = isArchived;
    }

    /// <summary>Provider-assigned numeric id. Not stable across a sandbox re-seed.</summary>
    public int Id { get; }

    /// <summary>The durable identifier for this plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in minor units (cents). $299.00 is <c>29900</c>.</summary>
    public long PriceInCents { get; }

    /// <summary>Recurring price as a currency amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit as reported by the provider (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; }

    public bool IsArchived { get; }

    /// <summary>A human readable billing cadence, e.g. "month" or "3 months".</summary>
    public string BillingCadence => Interval == 1
        ? IntervalUnit
        : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1}s", Interval, IntervalUnit);
}
