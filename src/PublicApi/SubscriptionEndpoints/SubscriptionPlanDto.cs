namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (a Maxio product) available for subscription.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Maxio product id.</summary>
    public long ProductId { get; set; }

    /// <summary>Stable API handle of the plan — pass this to POST api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal? TrialPrice { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when Maxio requires a payment method at signup.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}