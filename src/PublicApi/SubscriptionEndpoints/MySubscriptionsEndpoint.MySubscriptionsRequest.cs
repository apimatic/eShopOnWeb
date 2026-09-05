namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string UserName { get; init; }

    public MySubscriptionsRequest(string userName)
    {
        UserName = userName;
    }
}
