namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
