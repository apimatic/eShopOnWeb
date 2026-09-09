namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
