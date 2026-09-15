namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
}
