namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing provider's catalog;
/// eShopOnWeb never stores plans of its own.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan. This is the value callers pass to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO 4217 currency code the plan bills in.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 with "month").</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider demands a stored payment method before a subscription can start.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public int? TrialIntervalLength { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    /// <summary>Product family handle the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
