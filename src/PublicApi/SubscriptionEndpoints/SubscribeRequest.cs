namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for POST /api/subscriptions.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The API handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
