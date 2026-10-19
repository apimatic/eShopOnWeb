namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (one returned by GET api/subscription-plans).</summary>
    public string? PlanHandle { get; set; }
}
