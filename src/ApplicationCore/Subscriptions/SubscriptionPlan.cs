namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system's catalog.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit, as held by the billing system.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 code of the billing site's currency, e.g. USD.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals, e.g. 1 (month).</summary>
    public int Interval { get; init; }

    /// <summary>Unit the <see cref="Interval"/> is counted in: "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the plan's default price point, when the billing system exposes one.</summary>
    public string? PricePointHandle { get; init; }

    /// <summary>True when the shopper must supply a payment method before the plan can be started.</summary>
    public bool PaymentMethodRequired { get; init; }

    /// <summary>Length of the free trial, or null when the plan has no trial.</summary>
    public int? TrialInterval { get; init; }

    /// <summary>Unit the <see cref="TrialInterval"/> is counted in: "month" or "day".</summary>
    public string? TrialIntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
