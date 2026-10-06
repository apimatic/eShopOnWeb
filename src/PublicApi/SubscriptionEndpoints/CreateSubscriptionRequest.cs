namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to enroll the authenticated user in a subscription plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// API handle of the plan to subscribe to (see GET /api/subscription-plans).
    /// When omitted, the configured default plan (or the first available plan) is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}