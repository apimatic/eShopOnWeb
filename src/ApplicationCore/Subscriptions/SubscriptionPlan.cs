using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system's product catalog.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, long priceInCents, int interval, string intervalUnit)
    {
        Handle = Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        PriceInCents = Guard.Against.Negative(priceInCents, nameof(priceInCents));
        Interval = interval;
        IntervalUnit = Guard.Against.NullOrWhiteSpace(intervalUnit, nameof(intervalUnit));
    }

    /// <summary>The stable API handle. Numeric ids are reassigned by the billing system; handles are not.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; init; }

    public long PriceInCents { get; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals, e.g. 1 with "month" is monthly.</summary>
    public int Interval { get; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>True when the plan cannot be subscribed to without capturing a payment method first.</summary>
    public bool PaymentMethodRequired { get; init; }

    public string? PricePointHandle { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public bool HasTrial { get; init; }
}
