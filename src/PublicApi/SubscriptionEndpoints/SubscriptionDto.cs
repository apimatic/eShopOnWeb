namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string Reference { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }
}
