namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionResponse
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
    public decimal Amount { get; set; }
}
