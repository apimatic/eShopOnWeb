namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to, as returned by GET api/subscription-plans (e.g. "eshop-pro").
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
