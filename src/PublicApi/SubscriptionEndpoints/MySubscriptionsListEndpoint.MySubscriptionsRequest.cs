namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string Username { get; }

    public MySubscriptionsRequest(string username)
    {
        Username = username;
    }
}
