namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string BuyerReference { get; }

    public MySubscriptionsRequest(string buyerReference)
    {
        BuyerReference = buyerReference;
    }
}
