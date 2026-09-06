namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string userName)
    {
        UserName = userName;
    }

    /// <summary>The authenticated shopper, taken from the bearer token rather than the request.</summary>
    public string UserName { get; }
}
