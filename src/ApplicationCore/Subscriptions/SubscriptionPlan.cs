namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system's catalog.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan. This is what you subscribe with.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Handle of the product family (catalog) the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Recurring price in the smallest currency unit, to avoid rounding on the wire.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = "USD";

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (for example 1 month).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, for example "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the billing system refuses a signup that has no payment method on file.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Length of the free trial in <see cref="TrialIntervalUnit"/>s, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }
}
