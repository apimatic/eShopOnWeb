namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Internal request for listing the caller's subscriptions. Constructed by the endpoint from the JWT — it is
/// never model-bound from the HTTP request, so identity always comes from the token.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string? callerUserName)
    {
        CallerUserName = callerUserName;
    }

    public string? CallerUserName { get; }
}
