namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated by the route handler from the caller's JWT identity - not bound from the
    /// request body, and any client-supplied value is overwritten before use.
    /// </summary>
    public string BuyerEmail { get; set; } = string.Empty;
}
