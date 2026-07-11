namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionRequest : BaseRequest
{
    public string BuyerId { get; init; }

    public MySubscriptionRequest(string buyerId)
    {
        BuyerId = buyerId;
    }
}
