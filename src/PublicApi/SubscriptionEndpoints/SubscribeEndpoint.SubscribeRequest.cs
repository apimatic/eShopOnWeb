namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
