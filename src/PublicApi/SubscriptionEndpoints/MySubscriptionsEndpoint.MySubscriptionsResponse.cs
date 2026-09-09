namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
