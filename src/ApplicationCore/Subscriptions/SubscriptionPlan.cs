namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing provider's catalog.
/// eShopOnWeb never stores plans locally - the billing provider is the system of record.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan. This is the value callers pass when subscribing.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, for display.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code of the billing site.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider requires a stored payment method before a subscription can be created.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool HasTrial { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    /// <summary>Handle of the plan's default price point, when the provider exposes one.</summary>
    public string? PricePointHandle { get; init; }

    public string? PricePointName { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>
    /// Provider-assigned numeric id, surfaced for support and diagnostics only. It is not stable across
    /// catalog re-seeds, so it is never persisted or used to address the plan - <see cref="Handle"/> is.
    /// </summary>
    public long ProviderPlanId { get; init; }
}
