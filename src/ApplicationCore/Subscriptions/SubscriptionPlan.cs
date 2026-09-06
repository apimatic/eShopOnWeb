namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a billing-provider product.
/// </summary>
public sealed record SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price expressed in the minor unit of <see cref="Currency"/>.</summary>
    public required int PriceInCents { get; init; }

    /// <summary>ISO currency code of the billing site, when the provider exposes it.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 with "month").</summary>
    public required int Interval { get; init; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Provider identifier of the plan. Not stable across catalog re-seeds; prefer <see cref="Handle"/>.</summary>
    public required int ProviderProductId { get; init; }

    public int? PricePointId { get; init; }

    public string? PricePointName { get; init; }

    /// <summary>True when the provider requires a stored payment method before a subscription can be created.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    public bool Taxable { get; init; }

    public int? TrialPriceInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);

    public bool HasTrial => TrialInterval is > 0;
}
