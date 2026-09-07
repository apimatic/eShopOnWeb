namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public required int Id { get; set; }
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required long PriceInCents { get; set; }
    public required int Interval { get; set; }
    public required string IntervalUnit { get; set; }
    public decimal Price => PriceInCents / 100m;
}
