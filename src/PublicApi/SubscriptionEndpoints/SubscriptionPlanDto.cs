namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscription plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle; pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents (e.g. 29900).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Formatted price, e.g. "$299.00".</summary>
    public string PriceFormatted { get; set; } = string.Empty;

    /// <summary>Billing interval length, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
