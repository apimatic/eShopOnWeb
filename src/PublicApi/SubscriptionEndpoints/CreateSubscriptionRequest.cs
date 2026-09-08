namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
