namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for POST /api/subscriptions. Identity comes from the JWT, not the body.</summary>
public class SubscribeRequest
{
    /// <summary>Handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }
}
