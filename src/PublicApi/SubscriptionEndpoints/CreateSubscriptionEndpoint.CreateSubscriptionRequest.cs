namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (one of the handles returned by
    /// GET api/subscription-plans). Required.
    /// </summary>
    public string? PlanHandle { get; set; }
}
