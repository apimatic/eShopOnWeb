namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = "";
}
