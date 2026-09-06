namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A sellable recurring plan, projected from the billing system onto a provider-neutral shape.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan. This is the value callers subscribe with.</summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit. Null means the billing system did not report one.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (an annual plan is 12 months, not 1 year).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing period unit as reported by the billing system (for example <c>day</c> or <c>month</c>).</summary>
    public string? IntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off setup fee in the smallest currency unit.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>True when the billing system will refuse to enrol a customer that has no payment method on file.</summary>
    public bool RequiresCreditCard { get; set; }

    public bool? Taxable { get; set; }

    public string? ProductFamilyHandle { get; set; }
}
