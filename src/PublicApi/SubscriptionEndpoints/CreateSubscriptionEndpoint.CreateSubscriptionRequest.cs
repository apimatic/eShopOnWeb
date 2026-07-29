namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
