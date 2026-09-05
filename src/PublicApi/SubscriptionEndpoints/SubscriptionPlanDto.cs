namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public decimal PriceInCents { get; set; }
    public string? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? Description { get; set; }
}
