namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string? PlanHandle { get; set; }
}
