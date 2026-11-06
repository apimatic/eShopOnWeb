namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }
}
