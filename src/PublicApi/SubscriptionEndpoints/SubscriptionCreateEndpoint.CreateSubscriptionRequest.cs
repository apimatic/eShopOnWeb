namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Required -
    /// the API deliberately has no built-in default plan, so the same build works against any catalog.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes the call safely retryable. Send the same value with a retry of the
    /// same intent and at most one subscription is created. May also be supplied as the
    /// <c>Idempotency-Key</c> request header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
