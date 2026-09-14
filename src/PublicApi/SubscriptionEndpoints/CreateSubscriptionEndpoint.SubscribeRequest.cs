namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. It carries only what the shopper is allowed to choose - which
/// plan, and optionally an idempotency token. Who is subscribing comes from the bearer token.
/// </summary>
public class SubscribeRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional caller-generated token that makes a retry of this exact attempt return the same
    /// subscription. May also be sent as the <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
