namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Required: there is no default plan, because a handle that is right for one Maxio catalog is
    /// meaningless in another.
    /// </summary>
    public string? PlanHandle { get; set; }
}
