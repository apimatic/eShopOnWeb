namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; this is what you post to /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    public int PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the plan cannot be subscribed to without capturing a payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
