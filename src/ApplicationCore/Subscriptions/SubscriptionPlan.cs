namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Mirrors a Maxio Advanced Billing product
/// that lives in the configured product family.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is the stable identifier. Numeric ids are reassigned when a Maxio site is
/// re-seeded, so callers should always address a plan by its handle.
/// </remarks>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>The recurring price in the smallest unit of the site's currency.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The billing site's currency, e.g. "USD".</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period, e.g. 1 (month).</summary>
    public int Interval { get; init; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when Maxio requires a payment profile before the subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public string? PricePointName { get; init; }
    public string? ProductFamilyHandle { get; init; }

    public long TrialPriceInCents { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public long InitialChargeInCents { get; init; }
}
