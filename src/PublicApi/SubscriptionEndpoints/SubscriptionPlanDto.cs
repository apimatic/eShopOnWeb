namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can enroll in. Prices are carried in minor units (cents) exactly as
/// Maxio reports them, alongside a convenience decimal amount and a display currency.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? ProductFamilyName { get; set; }
}
