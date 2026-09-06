namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio Advanced Billing
/// product belonging to the configured product family.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers pass when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price expressed in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price expressed as a decimal amount, e.g. 299.00.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code of the billing site, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Unit the billing interval is expressed in, e.g. month or day.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the plan is sold with a trial period.</summary>
    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off charge applied at signup, in the smallest currency unit.</summary>
    public long InitialChargeInCents { get; set; }

    /// <summary>True when the billing provider requires a stored payment method before a subscription can start.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Name of the price point the plan is currently sold at.</summary>
    public string? PricePointName { get; set; }
}
