namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The buyer's identity. Always overwritten from the authenticated JWT before handling -
    /// never trust a client-supplied value here.
    /// </summary>
    public string BuyerEmail { get; set; } = string.Empty;
}
