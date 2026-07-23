namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string userReference)
    {
        UserReference = userReference;
    }

    public string UserReference { get; }
}
