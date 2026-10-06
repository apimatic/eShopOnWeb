namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
