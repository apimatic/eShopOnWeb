namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public decimal PriceInDollars => PriceInCents / 100m;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public bool RequireCreditCard { get; set; }
}
