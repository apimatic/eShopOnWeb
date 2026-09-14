namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Optional:
    /// when omitted the deployment's configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
