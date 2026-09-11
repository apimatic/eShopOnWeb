namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string CustomerReference { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
}
