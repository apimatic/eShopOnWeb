namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Set by the endpoint from the caller's JWT identity.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
