namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request for POST /api/subscriptions.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
