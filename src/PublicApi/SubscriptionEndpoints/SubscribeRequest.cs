namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest
{
    /// <summary>
    /// The plan (product) handle to subscribe to. When omitted, the default plan in the configured family
    /// (the lowest-priced non-archived product) is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
