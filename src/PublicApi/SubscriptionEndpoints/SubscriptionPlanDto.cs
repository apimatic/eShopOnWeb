namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string PriceUnit { get; set; } = "";
}
