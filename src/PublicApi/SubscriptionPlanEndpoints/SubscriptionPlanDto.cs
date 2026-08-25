namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public int? ProductId { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
