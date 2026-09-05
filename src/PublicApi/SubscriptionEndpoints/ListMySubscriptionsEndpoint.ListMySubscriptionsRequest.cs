namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string BuyerEmail { get; }

    public ListMySubscriptionsRequest(string buyerEmail)
    {
        BuyerEmail = buyerEmail;
    }
}
