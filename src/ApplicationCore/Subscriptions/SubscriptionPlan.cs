namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing system of record.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan. This, not the numeric id, is the contract.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Provider-assigned numeric id. Unstable across catalog re-seeds; informational only.</summary>
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code the plan is billed in (the billing site currency).</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/> periods in one billing cycle, e.g. 1.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit as reported by the provider, e.g. month or day.</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>True when the provider requires a stored payment method before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
