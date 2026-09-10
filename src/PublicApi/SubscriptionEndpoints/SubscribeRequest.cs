namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    /// <summary>
    /// The stable handle of the plan to subscribe to — one of the handles returned by
    /// <c>GET /api/subscription-plans</c> (e.g. "eshop-pro").
    /// </summary>
    public string? PlanHandle { get; set; }
}
