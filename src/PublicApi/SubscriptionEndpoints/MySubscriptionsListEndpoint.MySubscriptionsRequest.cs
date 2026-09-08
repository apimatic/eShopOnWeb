namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the authenticated token; never accept from the client.</summary>
    public string UserName { get; set; }
}
