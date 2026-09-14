namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}
