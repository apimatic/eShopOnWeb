namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to, e.g. <c>eshop-pro</c>.</summary>
    public string? PlanHandle { get; set; }
}
