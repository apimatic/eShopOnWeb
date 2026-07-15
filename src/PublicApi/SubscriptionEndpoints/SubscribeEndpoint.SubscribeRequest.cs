namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    // Populated from the caller's authenticated identity, never trusted from the request body.
    public string CustomerReference { get; set; } = string.Empty;
}
