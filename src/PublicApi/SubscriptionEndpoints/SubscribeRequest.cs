namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Use
    /// <c>GET /api/subscription-plans</c> to list available handles.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
