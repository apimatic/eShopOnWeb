namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Set server-side from the caller's JWT identity in <see cref="CreateSubscriptionEndpoint.AddRoute"/> —
    /// any value submitted by the client for this property is discarded before the request is handled.
    /// </summary>
    public string? Username { get; set; }
}
