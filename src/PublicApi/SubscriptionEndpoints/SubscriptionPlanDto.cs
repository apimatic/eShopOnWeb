namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to, as returned by GET /api/subscription-plans.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }

    /// <summary>Stable handle to pass to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable price + cadence, e.g. "299.00 / month".</summary>
    public string PriceFormatted { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }
}
