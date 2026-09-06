namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Optional
    /// only when the server is configured with a default plan; otherwise the request is rejected
    /// rather than a plan being chosen on the shopper's behalf.
    /// </summary>
    public string? PlanHandle { get; set; }
}
