namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string userName)
    {
        UserName = userName;
    }

    public string UserName { get; }
}
