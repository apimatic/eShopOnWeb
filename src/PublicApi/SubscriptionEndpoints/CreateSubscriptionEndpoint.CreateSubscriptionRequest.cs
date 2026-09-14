namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The shopper is taken from the bearer token, so the
/// only thing to say here is which plan — and, optionally, how to deduplicate the call.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional. Two requests from the same shopper carrying the same key resolve to the same
    /// subscription instead of creating a second one. May also be sent as an
    /// <c>Idempotency-Key</c> header. When omitted, the plan handle is used, so a double-click is
    /// safe without doing anything.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
