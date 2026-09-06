using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A purchasable recurring plan offered by the billing system of record.
/// Identified by its <see cref="Handle"/>; numeric provider ids are deliberately not exposed
/// because they are not stable across catalog re-seeds.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code the price is expressed in.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, as reported by the provider (e.g. "month", "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider requires a payment method before the plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public decimal Price => PriceInCents / 100m;
}
