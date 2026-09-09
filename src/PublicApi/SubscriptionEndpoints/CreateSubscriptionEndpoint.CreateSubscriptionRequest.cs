namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to create a subscription for the authenticated user.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan (Maxio product) to subscribe to, e.g. from GET api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
