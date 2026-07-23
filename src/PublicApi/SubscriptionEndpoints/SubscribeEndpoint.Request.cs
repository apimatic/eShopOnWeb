namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>The durable handle of the plan to enroll in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; }
}
