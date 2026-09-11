using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public decimal Price { get; set; }
}
