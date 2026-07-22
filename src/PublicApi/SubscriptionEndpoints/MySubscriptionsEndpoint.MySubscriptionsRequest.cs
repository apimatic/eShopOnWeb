namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Set from the caller's token.</summary>
    public string UserReference { get; set; }
}
