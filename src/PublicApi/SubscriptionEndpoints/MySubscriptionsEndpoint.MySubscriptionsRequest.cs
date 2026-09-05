namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string Username { get; init; }

    public MySubscriptionsRequest(string username)
    {
        Username = username;
    }
}
