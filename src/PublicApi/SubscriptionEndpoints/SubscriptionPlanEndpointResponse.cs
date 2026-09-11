namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanEndpointResponse
{
    public string PlanHandle { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "";
    public int Interval { get; set; }
    public string ComponentHandle { get; set; } = "";
    public string ComponentName { get; set; } = "";
}
