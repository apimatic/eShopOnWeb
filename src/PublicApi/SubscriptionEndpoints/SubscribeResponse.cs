namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
    public string Message { get; set; } = "";
}
