namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Required unless the deployment configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes the request replay-safe across a cancel-and-resubscribe cycle: two
    /// requests carrying the same key always resolve to the same subscription. It is not needed
    /// for duplicate protection - a caller is never enrolled twice in a plan they already hold.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
