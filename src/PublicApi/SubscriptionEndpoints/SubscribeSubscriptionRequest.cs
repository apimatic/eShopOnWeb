namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body for POST /api/subscriptions — the plan the shopper wants to subscribe to.</summary>
public class SubscribeSubscriptionRequest
{
    /// <summary>The stable API handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string? PlanHandle { get; set; }
}
