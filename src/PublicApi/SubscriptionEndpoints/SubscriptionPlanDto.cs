namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The recurring price in the site currency, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}
