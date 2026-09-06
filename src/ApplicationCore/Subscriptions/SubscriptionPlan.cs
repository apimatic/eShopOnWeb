namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing system.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>The stable API handle of the plan. This is the value callers subscribe with.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO-4217 code of the billing site's currency.</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public required int Interval { get; init; }

    /// <summary><c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    /// <summary>One-off charge applied at signup, when the plan defines one.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>
    /// True when the billing system will refuse a signup that has no payment profile on file.
    /// eShopOnWeb never captures payment instruments, so such plans cannot be subscribed to here.
    /// </summary>
    public required bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Billing-system identifier of the plan. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public required int Id { get; init; }

    public decimal Price => PriceInCents / 100m;
}
