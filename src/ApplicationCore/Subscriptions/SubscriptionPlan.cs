namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing system of record;
/// eShopOnWeb never keeps its own copy of the subscription catalog.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Identifier assigned by the billing system. Not stable across catalog re-seeds - prefer <see cref="Handle"/>.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (cents), which is how the billing system reports money.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Convenience projection of <see cref="PriceInCents"/> into major units.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code of the billing site.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with "month").</summary>
    public int Interval { get; init; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    public long? SetupFeeInCents { get; init; }

    /// <summary>
    /// True when the billing system refuses a signup that has no payment profile attached.
    /// This integration does not capture payment methods, so such plans cannot be subscribed to here.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }

    public string ProductFamilyHandle { get; init; } = string.Empty;

    public string? ProductFamilyName { get; init; }
}
