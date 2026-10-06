namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the current user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
