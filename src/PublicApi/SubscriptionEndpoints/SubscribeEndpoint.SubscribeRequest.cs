namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body for POST /api/subscriptions. The subscriber is taken from the token, not this body.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. one returned by GET /api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }
}
