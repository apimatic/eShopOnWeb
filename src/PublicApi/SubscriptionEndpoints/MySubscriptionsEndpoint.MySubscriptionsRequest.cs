namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Taken from the bearer token, never from the request.</summary>
    public string? UserReference { get; set; }
}
