namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>The caller's stable reference, taken from the JWT — set server-side.</summary>
    public string UserReference { get; set; } = string.Empty;
}
