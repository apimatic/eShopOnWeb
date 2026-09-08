namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseMessage
{
    /// <summary>
    /// The stable handle of the plan to subscribe to (from GET api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
