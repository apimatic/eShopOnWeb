namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity — never bound from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}
