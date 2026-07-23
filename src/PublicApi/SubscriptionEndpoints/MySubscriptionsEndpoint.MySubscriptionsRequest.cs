namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string UserReference { get; init; }

    public MySubscriptionsRequest(string userReference)
    {
        UserReference = userReference;
    }
}
