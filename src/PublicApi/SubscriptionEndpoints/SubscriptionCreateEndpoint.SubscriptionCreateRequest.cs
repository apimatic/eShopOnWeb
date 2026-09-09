namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class SubscriptionCreateRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (from GET api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
