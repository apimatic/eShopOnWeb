namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. "eshop-pro"). Optional: when omitted the default
    /// plan of the configured product family is used. The subscriber is always taken from the JWT.
    /// </summary>
    public string? PlanHandle { get; set; }
}
