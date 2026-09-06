namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, from GET /api/subscription-plans. Optional: when omitted,
    /// the plan configured as "Maxio:DefaultPlanHandle" is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
