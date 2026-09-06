namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal PriceInDollars { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}
