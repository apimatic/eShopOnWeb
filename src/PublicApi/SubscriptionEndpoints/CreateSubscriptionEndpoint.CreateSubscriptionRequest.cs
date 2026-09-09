namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. a handle from GET api/subscription-plans).
    /// When omitted, the cheapest plan in the configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
