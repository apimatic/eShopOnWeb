namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set by the route handler from the authenticated caller's identity.</summary>
    public string UserName { get; set; } = string.Empty;
}
