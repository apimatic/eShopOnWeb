namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier to send back to POST api/subscriptions.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>How often the plan renews, e.g. <c>1 month</c>.</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before this plan can be subscribed to.</summary>
    public bool PaymentMethodRequired { get; set; }
}
