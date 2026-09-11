namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
}
