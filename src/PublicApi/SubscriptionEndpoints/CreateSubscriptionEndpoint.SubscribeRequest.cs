namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Optional: when omitted the store's configured default plan is used.
    /// </summary>
    public string PlanHandle { get; set; }
}
