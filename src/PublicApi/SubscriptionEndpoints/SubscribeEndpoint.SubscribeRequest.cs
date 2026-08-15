namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Handles are stable across
    /// re-seeds, unlike numeric ids.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
