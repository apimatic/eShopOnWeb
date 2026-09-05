namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string Username { get; init; }

    public ListMySubscriptionsRequest(string username)
    {
        Username = username;
    }
}
