namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio API handle of the plan (Product) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
