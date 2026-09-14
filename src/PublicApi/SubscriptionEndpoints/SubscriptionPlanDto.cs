namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable identifier of the plan; pass this to POST /api/subscriptions. Numeric billing ids are
    /// intentionally not exposed because they change whenever the billing catalog is re-seeded.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing period, e.g. "every month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public string? ProductFamilyHandle { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off signup charge in minor units, when the plan has one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>
    /// True when the billing provider requires a stored payment method before a signup on this plan
    /// can succeed.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }
}
