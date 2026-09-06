namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. Scopes idempotency for this request. When omitted, repeated requests for the same
    /// plan resolve to the same subscription, so a double-click cannot create - or bill - a second
    /// one. Supply a distinct value to deliberately create an additional subscription, for example
    /// when re-subscribing to a plan that was cancelled.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
