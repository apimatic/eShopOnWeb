using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
