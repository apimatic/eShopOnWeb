namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the caller's JWT.</summary>
    public string BuyerId { get; set; } = string.Empty;
}
