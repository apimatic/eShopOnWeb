namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan a shopper can enrol in.</summary>
public class SubscriptionPlanDto
{
    public int? ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable price, e.g. "$299.00 / month".</summary>
    public string PriceDisplay { get; set; } = string.Empty;
}
