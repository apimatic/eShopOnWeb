namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. This is a provider-agnostic view of a
/// billing "product" — the billing system of record (Maxio) owns the authoritative data.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier used to subscribe to the plan.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Display name of the plan.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Recurring price expressed in the minor currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code the price is expressed in (e.g. "USD").</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Length of a billing period, measured in <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; init; }

    /// <summary>Unit the billing period is measured in (e.g. "month", "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;
}
