namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Price { get; set; } = "";
    public string Description { get; set; } = "";
    public string BillingCycle { get; set; } = "";
}
