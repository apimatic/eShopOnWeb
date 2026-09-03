namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for POST /api/subscriptions. The subscriber is taken from the caller's token, never the
/// body, so a caller can only ever subscribe themselves.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Optional; when omitted the
    /// configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
