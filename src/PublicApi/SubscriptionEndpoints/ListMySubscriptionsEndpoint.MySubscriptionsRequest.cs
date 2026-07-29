namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for the current user's subscriptions. The user reference is taken from the
/// authenticated token, never from the request payload.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string userReference)
    {
        UserReference = userReference;
    }

    public string UserReference { get; }
}
