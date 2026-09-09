namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscription plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle to pass to POST /api/subscriptions, e.g. "eshop-pro".</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Human-friendly price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
