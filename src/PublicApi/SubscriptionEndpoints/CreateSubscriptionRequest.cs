namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to enroll the authenticated shopper in a plan. Only the plan is specified by the
/// caller; the shopper's identity is taken from the JWT, never from the request body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
