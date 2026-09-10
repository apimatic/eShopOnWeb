namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. Optional — when omitted, the first available plan in the
    /// configured product family is used. The subscriber's identity comes from the JWT, never here.
    /// </summary>
    public string? PlanHandle { get; set; }
}
