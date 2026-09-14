namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The subscriber is taken from the bearer token, never from
/// the body.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Optional:
    /// when omitted the configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
