namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (a Maxio product handle, e.g. "eshop-pro").
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
