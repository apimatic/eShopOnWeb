namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
