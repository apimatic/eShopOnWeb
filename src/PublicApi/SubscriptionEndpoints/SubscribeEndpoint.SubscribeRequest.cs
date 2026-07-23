namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enrol in, for example <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
