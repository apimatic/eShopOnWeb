using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio Advanced Billing
/// <c>Product</c> (see the <c>Product</c> schema in the Maxio OpenAPI specification).
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan. This is the identifier callers subscribe with.</summary>
    public required string Handle { get; init; }

    /// <summary>Display name of the plan.</summary>
    public required string Name { get; init; }

    /// <summary>Optional marketing description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in major units.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period (e.g. 1 with "month").</summary>
    public required int Interval { get; init; }

    /// <summary>Billing period unit &#8212; "day" or "month" per the Interval-Unit enum in the specification.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Handle of the default price point of the plan, when the provider exposes one.</summary>
    public string? PricePointHandle { get; init; }

    /// <summary>True when a payment profile must be captured before the shopper can subscribe.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    /// <summary>Length of the free trial, in <see cref="TrialIntervalUnit"/>s, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    /// <summary>Unit of <see cref="TrialInterval"/>.</summary>
    public string? TrialIntervalUnit { get; init; }

    /// <summary>One-off charge applied at signup, in cents, when the plan has one.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>Whether the plan is subject to tax.</summary>
    public bool Taxable { get; init; }

    /// <summary>When the plan was archived by the merchant; <c>null</c> for active plans.</summary>
    public DateTimeOffset? ArchivedAt { get; init; }
}
