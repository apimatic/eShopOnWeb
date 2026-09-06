namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The shopper is never named here - their identity comes
/// from the bearer token.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. When
    /// omitted, the deployment's configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes this signup exactly replayable. Repeating the call with the same key
    /// returns the subscription the first call produced instead of creating another. When omitted,
    /// the plan handle is used, so a double-clicked subscribe button is still a no-op.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional first name for the billing customer. Defaults to one derived from the shopper's email.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name for the billing customer. Defaults to one derived from the shopper's email.</summary>
    public string? LastName { get; set; }

    /// <summary>Optional organisation for the billing customer.</summary>
    public string? Organization { get; set; }
}
