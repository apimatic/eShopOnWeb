namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public string Currency { get; set; } = "USD";
}
