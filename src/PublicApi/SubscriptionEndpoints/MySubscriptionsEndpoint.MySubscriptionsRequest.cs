namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set from the bearer token's identity, never from the request.</summary>
    public string? UserReference { get; set; }
}
