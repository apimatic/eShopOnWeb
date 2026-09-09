namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
