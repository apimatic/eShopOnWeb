namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan. The shopper is taken from the bearer token, never
/// from the body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// May be omitted only when a single plan is published.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional name for the billing customer created on first subscribe. When omitted, a name is
    /// derived from the shopper's email address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <inheritdoc cref="FirstName"/>
    public string? LastName { get; set; }

    /// <summary>Optional organisation for the billing customer created on first subscribe.</summary>
    public string? Organization { get; set; }
}
