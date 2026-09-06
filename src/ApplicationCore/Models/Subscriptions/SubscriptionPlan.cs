namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Sourced from the billing system of record;
/// eShopOnWeb never stores plan definitions locally.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/> (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO 4217 currency code of the billing site.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with "month" = monthly).</summary>
    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the shopper must supply a payment method before the plan can be started.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool Taxable { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? SetupFeeInCents { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    public decimal Price => PriceInCents / 100m;
}
