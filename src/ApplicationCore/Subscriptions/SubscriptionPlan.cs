namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing provider.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price, expressed in the minor unit of <see cref="Currency"/>.</summary>
    public required int PriceInCents { get; init; }

    /// <summary>Recurring price, expressed in the major unit of <see cref="Currency"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code, e.g. <c>USD</c>.</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period, e.g. <c>1</c> for monthly.</summary>
    public required int Interval { get; init; }

    /// <summary>Unit of the billing period, e.g. <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Handle of the product family that groups this plan.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// True when the provider refuses to enroll a customer that has no stored payment method.
    /// Subscribing to such a plan requires capturing a card first, which this integration does not do.
    /// </summary>
    public required bool RequiresPaymentMethod { get; init; }

    public int? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public bool HasTrial => TrialInterval is > 0;
}
