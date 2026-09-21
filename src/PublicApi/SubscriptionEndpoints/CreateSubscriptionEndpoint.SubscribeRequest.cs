namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for subscribing the caller to a plan.</summary>
public class SubscribeRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Optional: when omitted, the server's
    /// configured default plan handle is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
