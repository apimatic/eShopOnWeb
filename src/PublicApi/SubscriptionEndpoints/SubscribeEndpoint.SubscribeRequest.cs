namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set by the route handler from the authenticated caller's identity — never bound from the request body.</summary>
    public string UserName { get; set; } = string.Empty;
}
