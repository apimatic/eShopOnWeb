namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsRequest : BaseRequest
{
    public string Username { get; }

    public GetMySubscriptionsRequest(string username)
    {
        Username = username;
    }
}
