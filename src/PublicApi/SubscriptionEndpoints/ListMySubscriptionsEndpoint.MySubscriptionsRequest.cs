namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string UserId { get; }

    public MySubscriptionsRequest(string userId)
    {
        UserId = userId;
    }
}
