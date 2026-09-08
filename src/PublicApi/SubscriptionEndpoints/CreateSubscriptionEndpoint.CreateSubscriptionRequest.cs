namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
