namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan key. Pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO currency code of the billing site.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? IntervalCount { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the plan cannot be subscribed to without a payment method on file.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
