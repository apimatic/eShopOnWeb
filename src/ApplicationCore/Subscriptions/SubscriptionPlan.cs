namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
/// <remarks>
/// Projected from a billing-provider product. Amounts are exposed both as integer minor units
/// (the provider's canonical representation) and as a decimal, so callers never have to divide.
/// </remarks>
public sealed record SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers pass to subscribe.</summary>
    public required string Handle { get; init; }

    /// <summary>Provider assigned numeric id. Not stable across catalog re-seeds; never persist it.</summary>
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (e.g. cents).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount in <see cref="Currency"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code the plan bills in.</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public required int Interval { get; init; }

    /// <summary>"month" or "day".</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>True when the provider requires a stored payment method before signup can succeed.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    public bool HasTrial => TrialInterval is > 0;

    public long? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    /// <summary>Handle of the product family (catalog) the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Handle of the plan's default price point, when the provider exposes one.</summary>
    public string? PricePointHandle { get; init; }
}
