namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// The authenticated caller's username, set by the endpoint from the JWT.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public MySubscriptionsRequest(string username)
    {
        Username = username;
    }
}
