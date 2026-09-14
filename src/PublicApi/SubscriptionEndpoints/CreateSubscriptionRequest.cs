namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to enroll the authenticated user in a subscription plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
