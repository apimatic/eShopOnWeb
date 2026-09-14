namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle of the plan. Pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code, when the billing site reports one.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable price, e.g. "$299.00 / month".</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Handle of the plan's default price point, when the billing system exposes one.</summary>
    public string? PricePointHandle { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
