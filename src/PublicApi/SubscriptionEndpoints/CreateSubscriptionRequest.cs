using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the subscription plan to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
