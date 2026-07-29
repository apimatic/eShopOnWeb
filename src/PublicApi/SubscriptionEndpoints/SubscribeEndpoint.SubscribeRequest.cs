namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Optional — when omitted,
    /// the first plan exposed by the configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
