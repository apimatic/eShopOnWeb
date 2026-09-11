namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; }
}

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
