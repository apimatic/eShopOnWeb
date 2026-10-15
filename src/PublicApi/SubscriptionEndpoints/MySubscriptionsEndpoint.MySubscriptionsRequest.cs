namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(BuyerIdentity? buyer)
    {
        Buyer = buyer;
    }

    public BuyerIdentity? Buyer { get; }
}
