namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Body of POST /api/subscriptions.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The stable Maxio product handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
