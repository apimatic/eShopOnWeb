using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A plan a shopper can subscribe to, as published by the billing system.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is the stable identifier; the billing system reassigns numeric ids when its catalog
/// is re-seeded, so no numeric id is carried into eShopOnWeb.
/// </remarks>
public sealed record SubscriptionPlan
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents). The billing system speaks in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code of the billing site, when it could be read.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? Interval { get; init; }

    /// <summary>Billing period unit, as published by the billing system (for example <c>month</c>).</summary>
    public string? IntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    /// <summary>True when the plan cannot be subscribed to without capturing a payment method first.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public int? ExpirationInterval { get; init; }

    public string? ExpirationIntervalUnit { get; init; }

    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public decimal? TrialPrice => TrialPriceInCents is null ? null : decimal.Divide(TrialPriceInCents.Value, 100m);
}
