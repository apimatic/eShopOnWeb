namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as exposed by the API.
/// </summary>
public sealed class SubscriptionPlanDto
{
    /// <summary>Stable plan handle - pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in cents (e.g. 29900 for $299.00).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price, e.g. "299.00".</summary>
    public string Price { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = "month";

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public bool PaymentMethodRequired { get; set; }
}
