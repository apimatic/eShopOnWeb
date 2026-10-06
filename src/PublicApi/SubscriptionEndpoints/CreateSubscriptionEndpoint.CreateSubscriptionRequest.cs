namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the subscription plan to subscribe to (see GET api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}