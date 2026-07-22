namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; }

    /// <summary>Set from the bearer token, never from the request body.</summary>
    public string BuyerId { get; set; }
}
