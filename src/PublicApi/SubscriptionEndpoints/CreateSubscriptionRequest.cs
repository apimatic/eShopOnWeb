namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public sealed class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
