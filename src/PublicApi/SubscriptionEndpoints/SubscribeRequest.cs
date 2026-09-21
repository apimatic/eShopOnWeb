namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <c>POST /api/subscriptions</c>. The subscriber's identity is taken from the JWT, never the body.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The plan (product) handle to subscribe to. Optional: when omitted, the configured default plan — or
    /// the first available plan — is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
