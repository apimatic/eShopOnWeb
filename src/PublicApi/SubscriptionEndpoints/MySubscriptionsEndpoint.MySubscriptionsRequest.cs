namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    // Populated from the caller's authenticated identity.
    public string CustomerReference { get; set; } = string.Empty;
}
