namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Handles are used rather than numeric ids because only handles are stable across catalog re-seeds.
    /// </summary>
    public string? PlanHandle { get; set; }
}
