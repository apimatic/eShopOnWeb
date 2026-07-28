namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscribable plan.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle; pass this to <c>POST /api/subscriptions</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>ISO currency code (e.g. USD). May be empty if the site currency is unavailable.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (<c>day</c> or <c>month</c>).</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
