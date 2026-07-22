namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to enrol in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; }

    /// <summary>Set from the caller's token; anything supplied in the body is overwritten.</summary>
    public string UserReference { get; set; }
}
