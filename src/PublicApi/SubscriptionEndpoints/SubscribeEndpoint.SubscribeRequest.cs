namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The Maxio plan (product) handle to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set by the endpoint from the caller's JWT, not from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Set by the endpoint from the caller's JWT, not from the request body.</summary>
    public string Email { get; set; } = string.Empty;
}
