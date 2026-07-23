namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The eShopOnWeb user (username/email) to enroll.</summary>
    public string UserReference { get; set; }

    /// <summary>The durable handle of the plan to enroll in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; }
}
