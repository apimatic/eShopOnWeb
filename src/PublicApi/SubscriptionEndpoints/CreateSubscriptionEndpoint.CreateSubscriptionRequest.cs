namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for POST api/subscriptions. Only the plan to subscribe to is supplied;
/// the customer identity is taken from the authenticated caller's token.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (from GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }
}
