using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Mirrors a "product" in the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        string currency, int? intervalLength, string? intervalUnit, bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        IntervalLength = intervalLength;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>
    /// Stable, human-readable identifier of the plan. This - not the numeric id - is what callers
    /// subscribe to, because the billing system reassigns numeric ids when a catalog is re-seeded.
    /// </summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    /// <summary>Recurring price in major currency units (e.g. 299.00).</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    /// <summary>ISO 4217 currency code the plan is billed in.</summary>
    public string Currency { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int? IntervalLength { get; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; }

    /// <summary>True when the billing system refuses signup unless a payment method is captured.</summary>
    public bool RequiresPaymentMethod { get; }
}
