namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public long ProductId { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}
