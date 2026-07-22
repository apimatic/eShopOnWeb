using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to, normalized from the billing provider's product catalog.
/// Prices are expressed in whole currency units (dollars); the provider's minor-unit representation is
/// converted once, inside the billing client.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        string? description,
        decimal price,
        int interval,
        BillingIntervalUnit intervalUnit,
        bool requiresPaymentMethod,
        bool isArchived)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        IsArchived = isArchived;
    }

    /// <summary>Provider-assigned numeric identifier. Not stable across a sandbox re-seed.</summary>
    public int Id { get; }

    /// <summary>The durable identifier the integration is configured with.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price per billing period, in whole currency units.</summary>
    public decimal Price { get; }

    /// <summary>Number of <see cref="IntervalUnit"/> units in one billing period.</summary>
    public int Interval { get; }

    public BillingIntervalUnit IntervalUnit { get; }

    public bool RequiresPaymentMethod { get; }

    public bool IsArchived { get; }

    /// <summary>Human readable billing period, e.g. "month" or "3 months".</summary>
    public string BillingPeriodDescription => Interval == 1
        ? IntervalUnit.ToString().ToLowerInvariant()
        : $"{Interval} {IntervalUnit.ToString().ToLowerInvariant()}s";
}
