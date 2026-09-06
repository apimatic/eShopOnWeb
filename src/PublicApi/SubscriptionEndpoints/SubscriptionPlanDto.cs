namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle of the plan. Pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900 for $299.00.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO currency code the plan bills in.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit: <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable billing period, e.g. <c>every month</c>.</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when a payment method has to be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }

    public long? SetupFeeInCents { get; set; }

    public bool Taxable { get; set; }

    public string? PricePointName { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }
}
