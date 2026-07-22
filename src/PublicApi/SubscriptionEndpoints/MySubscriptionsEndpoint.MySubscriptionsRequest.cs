namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string BuyerId { get; init; }

    public MySubscriptionsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}
