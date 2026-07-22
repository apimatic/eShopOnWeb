namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Handle of the plan to enroll in. The configured default plan is used when omitted.</summary>
    public string PlanHandle { get; set; }
}
