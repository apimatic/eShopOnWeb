namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class ListMySubscriptionsResponse
{
    public System.Collections.Generic.List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
