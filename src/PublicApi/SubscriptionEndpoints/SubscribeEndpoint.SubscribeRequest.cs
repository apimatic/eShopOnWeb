namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Overwritten server-side from the authenticated principal — never trust a client-supplied value.</summary>
    public string UserReference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;
}
