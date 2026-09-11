namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionDto[] Subscriptions { get; set; } = System.Array.Empty<MySubscriptionDto>();
}
