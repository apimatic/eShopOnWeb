using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The shopper is taken from the bearer token, so the body
/// only says what to subscribe to.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// Optional when the server configures a default plan.
    /// </summary>
    [StringLength(100, MinimumLength = 1)]
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional idempotency key. Repeating a subscribe call with the same key returns the
    /// subscription created by the first call instead of creating another one.
    /// </summary>
    [StringLength(64, MinimumLength = 1)]
    public string? IdempotencyKey { get; set; }
}
