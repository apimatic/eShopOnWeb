namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Set by the endpoint from the authenticated caller's identity — never trust a client-supplied value.</summary>
    public string UserId { get; set; } = string.Empty;
}
