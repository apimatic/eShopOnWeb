using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Mirrors the billing system's notion of a product
/// inside a product family.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    /// <summary>Billing-system identifier. Not stable across catalog re-seeds; never persist it.</summary>
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (cents).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in major currency units.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period (e.g. 1 with "month").</summary>
    public required int Interval { get; init; }

    /// <summary>"day" or "month".</summary>
    public required string IntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? InitialChargeInCents { get; init; }

    /// <summary>When true, a payment profile has to be captured before a subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool Taxable { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public string? ProductFamilyName { get; init; }

    public string? PricePointHandle { get; init; }

    public string? PricePointName { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
