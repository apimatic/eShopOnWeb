namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to (e.g. "eshop-pro"), as returned
    /// by GET api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
