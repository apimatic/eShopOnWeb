namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for <c>POST /api/subscriptions</c>. The identity of the shopper comes from the
/// JWT bearer token; only the plan handle is supplied by the caller.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
