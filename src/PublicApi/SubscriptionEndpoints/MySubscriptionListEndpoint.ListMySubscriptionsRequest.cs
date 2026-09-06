namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string userName)
    {
        UserName = userName;
    }

    /// <summary>Taken from the bearer token, never from the request.</summary>
    public string UserName { get; }
}
