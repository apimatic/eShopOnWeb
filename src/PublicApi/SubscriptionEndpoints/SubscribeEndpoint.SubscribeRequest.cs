namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <c>POST /api/subscriptions</c>. The subscriber's identity is taken from the
/// bearer token, not this payload.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. "eshop-pro"). Optional: when omitted,
    /// the lowest-priced available plan in the configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
