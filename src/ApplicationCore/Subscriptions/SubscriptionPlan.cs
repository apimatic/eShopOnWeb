namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system catalog.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (for example, cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code of the billing site, when it could be determined.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, for example <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>
    /// True when the billing system refuses signups for this plan without a stored payment
    /// method. This integration does not capture cards, so such plans cannot be subscribed to.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public string? PricePointHandle { get; init; }

    public long? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public decimal Price => PriceInCents / 100m;
}
