namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Recurring price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }
    /// <summary>Recurring price in minor currency units (e.g. cents).</summary>
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
