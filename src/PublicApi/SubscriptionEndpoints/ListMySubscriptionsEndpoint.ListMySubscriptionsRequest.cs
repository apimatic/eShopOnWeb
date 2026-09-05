namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Populated from the authenticated caller's identity (JWT) - never trust a client-supplied value here.
    /// </summary>
    public string UserReference { get; set; } = string.Empty;
}
