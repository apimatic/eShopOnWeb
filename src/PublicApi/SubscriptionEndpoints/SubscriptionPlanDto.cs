namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BillingIntervalCount { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
}
