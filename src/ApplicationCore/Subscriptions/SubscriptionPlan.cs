namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing system's catalog;
/// the handle - not the numeric id - is the stable identifier callers should use.
/// </summary>
public record SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO 4217 code of the billing site's currency, e.g. "USD".</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals, e.g. 1 with "month".</summary>
    public required int Interval { get; init; }
    public required string IntervalUnit { get; init; }

    /// <summary>True when the billing system requires a stored payment method before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public long? TrialPriceInCents { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public bool HasTrial => TrialInterval is > 0;

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;
}
