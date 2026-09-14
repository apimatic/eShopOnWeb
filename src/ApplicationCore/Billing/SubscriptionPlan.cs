namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring plan a shopper can enrol in, projected from the billing provider's catalogue.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier. This is what callers pass in order to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code the plan is billed in.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the provider requires a stored payment method before the plan can start.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Price charged during the trial, when the plan has one.</summary>
    public long? TrialPriceInCents { get; set; }

    /// <summary>Length of the trial in <see cref="TrialIntervalUnit"/>s, when the plan has one.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Handle of the price point the plan is quoted at.</summary>
    public string? PricePointHandle { get; set; }
}
