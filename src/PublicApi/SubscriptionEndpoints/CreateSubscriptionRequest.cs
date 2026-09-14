namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for POST api/subscriptions
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The Maxio product handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
