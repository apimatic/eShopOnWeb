namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string? UserName { get; }

    public ListMySubscriptionsRequest(string? userName)
    {
        UserName = userName;
    }
}
