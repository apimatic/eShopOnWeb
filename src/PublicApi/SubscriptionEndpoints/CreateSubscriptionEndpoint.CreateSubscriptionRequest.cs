namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes the call safely repeatable. Repeating a request with the same key
    /// returns the subscription the first call created instead of creating a second one. May also be
    /// supplied as the <c>Idempotency-Key</c> request header; the body value wins when both are set.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
