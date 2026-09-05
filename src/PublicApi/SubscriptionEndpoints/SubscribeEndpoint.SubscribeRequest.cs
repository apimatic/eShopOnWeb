namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller's identity, taken from the JWT - never bound from the request body.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
