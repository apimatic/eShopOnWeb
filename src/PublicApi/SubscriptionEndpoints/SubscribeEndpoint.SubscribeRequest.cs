namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Bound from the JSON request body.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Populated from the caller's JWT claims, not from client input.</summary>
    public string BuyerReference { get; set; } = string.Empty;

    /// <summary>Populated from the caller's JWT claims, not from client input.</summary>
    public string Email { get; set; } = string.Empty;
}
