namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// API handle of the plan (Maxio product handle) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
