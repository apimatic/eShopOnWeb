namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Interval { get; set; }
}
