using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Plans live in the billing system of record,
/// they are not part of the eShopOnWeb catalog.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        int interval, string intervalUnit, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>
    /// Stable, human readable identifier of the plan. Numeric ids are reassigned when a billing
    /// site is re-seeded, so the handle is the only safe way to address a plan.
    /// </summary>
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public long PriceInCents { get; }

    /// <summary>Recurring price expressed in whole currency units, e.g. 299.00.</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals, e.g. 1 (month).</summary>
    public int Interval { get; }
    public string IntervalUnit { get; }

    /// <summary>True when the billing system refuses a signup that has no payment profile on file.</summary>
    public bool RequiresPaymentMethod { get; }
}
