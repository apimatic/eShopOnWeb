namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body for POST /api/subscriptions. The subscriber is taken from the token, never the body.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
