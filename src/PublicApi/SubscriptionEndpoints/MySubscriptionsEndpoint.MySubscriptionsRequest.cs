namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
}
