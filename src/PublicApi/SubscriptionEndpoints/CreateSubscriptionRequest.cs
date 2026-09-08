namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from the subscription plans list).</summary>
    public string? PlanHandle { get; set; }
}
