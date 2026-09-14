namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API view of a subscription plan available for enrollment.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    /// <summary>Human-readable price, e.g. "$299.00 / month".</summary>
    public string PriceDisplay { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
