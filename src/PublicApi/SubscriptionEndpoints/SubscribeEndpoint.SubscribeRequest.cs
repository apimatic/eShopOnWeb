namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
