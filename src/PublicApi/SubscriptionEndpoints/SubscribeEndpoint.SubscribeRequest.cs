namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. "eshop-pro", "basic-plan") - see
    /// GET api/subscription-plans for the available handles.
    /// </summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// Set by the endpoint from the caller's JWT identity - not bound from the request body.
    /// </summary>
    public string BuyerId { get; set; }
}
