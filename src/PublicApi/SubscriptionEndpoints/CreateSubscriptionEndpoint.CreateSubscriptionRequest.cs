namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Optional
    /// only when the deployment configures <c>Maxio:DefaultPlanHandle</c>.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional. Used when the billing customer is created for the first time; otherwise a name is
    /// derived from the caller's email. The subscriber is always taken from the bearer token, never
    /// from this request.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional. See <see cref="FirstName"/>.</summary>
    public string? LastName { get; set; }
}
