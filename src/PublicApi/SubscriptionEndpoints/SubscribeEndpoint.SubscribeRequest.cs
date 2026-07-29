namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro"). Obtain valid handles from GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }
}
