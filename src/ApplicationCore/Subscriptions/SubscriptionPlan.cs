using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to. Projected from a billing-provider product.
/// </summary>
public class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price rendered as a decimal amount, e.g. <c>299.00</c>.</summary>
    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period, e.g. <c>1</c>.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the billing provider demands a stored payment method before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>One-off charge applied at signup, in cents, when configured.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>Length of the trial period, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    /// <summary>Name of the price point the plan is priced from.</summary>
    public string? PricePointName { get; init; }

    /// <summary>
    /// The billing provider's numeric product id. Unstable across catalog re-seeds - never persist
    /// or configure it; <see cref="Handle"/> is the durable identifier.
    /// </summary>
    public int ProviderProductId { get; init; }
}
