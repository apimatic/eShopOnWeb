namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = "";
    public string Message { get; set; } = "";
}
