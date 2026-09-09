namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can enroll in.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Human-readable price + period, e.g. "299.00 per month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;
}
