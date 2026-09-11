namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListRequest : BaseRequest
{
    public string UserName { get; init; }

    public SubscriptionListRequest(string userName)
    {
        UserName = userName;
    }

    public SubscriptionListRequest()
    {
        UserName = string.Empty;
    }
}
