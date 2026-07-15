namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string UserReference { get; }

    public MySubscriptionsRequest(string userReference)
    {
        UserReference = userReference;
    }
}
