namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The plan to subscribe to — the plan's handle as returned by
    /// GET api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
