using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A subscribable recurring plan offered by the billing provider.
/// Identified by <see cref="Handle"/> — provider numeric ids are not stable across catalog re-seeds.
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code the site bills in, e.g. <c>USD</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit as the provider reports it, e.g. <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; init; }

    public bool HasTrial { get; init; }
    public long? TrialPriceInCents { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }

    /// <summary>One-off charge applied at signup, in cents.</summary>
    public long SetupFeeInCents { get; init; }

    /// <summary>
    /// True when the provider will refuse a subscription for this plan unless payment details are
    /// supplied. Subscribing to such a plan is rejected up front rather than round-tripped.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }
}
