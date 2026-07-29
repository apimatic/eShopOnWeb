namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for <c>POST /api/subscriptions</c>. The subscriber is taken from the
/// JWT, so the body only carries the plan to subscribe to.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
