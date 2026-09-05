namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string BuyerReference { get; init; }

    public MySubscriptionsRequest(string buyerReference)
    {
        BuyerReference = buyerReference;
    }
}
