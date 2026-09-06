namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// Required: there is no default plan, because the catalog differs per deployment.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional idempotency key, also accepted as the <c>Idempotency-Key</c> request header.
    /// Two requests carrying the same key always resolve to the same subscription.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
