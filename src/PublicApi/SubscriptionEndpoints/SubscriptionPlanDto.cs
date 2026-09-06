namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan on offer, as returned by <c>GET api/subscription-plans</c>.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Pass this as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period, e.g. 1 with "month" for monthly.</summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Length of the free trial, or <c>null</c> when the plan has none.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when the billing system requires a stored payment method before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
