using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
}

public class MySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
