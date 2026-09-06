namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for <c>POST api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET api/subscription-plans</c>.
    /// Optional: when omitted the configured default plan is used, or the only plan on offer when
    /// there is exactly one.
    /// </summary>
    public string? PlanHandle { get; set; }
}
