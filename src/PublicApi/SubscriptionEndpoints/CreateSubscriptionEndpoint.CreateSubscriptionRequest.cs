namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST api/subscriptions.
/// </summary>
/// <remarks>
/// There is deliberately no customer or user field: the subscriber is taken from the bearer token,
/// so one shopper can never subscribe another.
/// </remarks>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET api/subscription-plans. Optional only
    /// when the deployment configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }
}
