using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; } = "";
}

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string PlanName { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string Price { get; set; } = "";
    public string NextBillingDate { get; set; } = "";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}
