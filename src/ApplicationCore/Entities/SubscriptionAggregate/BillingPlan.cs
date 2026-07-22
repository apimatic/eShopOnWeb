using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as exposed by the billing provider.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id,
        string handle,
        string name,
        string? description,
        decimal price,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.Negative(price, nameof(price));

        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>Provider-assigned numeric identifier. Not stable across a sandbox re-seed.</summary>
    public int Id { get; private set; }

    /// <summary>The stable identifier used in configuration and code (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Recurring price expressed in whole currency units (dollars), never cents.</summary>
    public decimal Price { get; private set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; private set; }

    /// <summary>The billing period unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; private set; }

    public bool RequiresPaymentMethod { get; private set; }

    /// <summary>Human readable billing cadence, e.g. <c>$299.00 / month</c>.</summary>
    public string BillingDescription => Interval == 1
        ? $"{Price:C} / {IntervalUnit}"
        : $"{Price:C} / {Interval} {IntervalUnit}s";
}
