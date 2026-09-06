namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
}
