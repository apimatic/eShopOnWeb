namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of one of the plans returned by GET api/subscription-plans. Required.</summary>
    public string? PlanHandle { get; set; }
}
