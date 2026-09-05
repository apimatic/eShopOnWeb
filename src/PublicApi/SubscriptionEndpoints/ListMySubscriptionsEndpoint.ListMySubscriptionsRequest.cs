namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Populated by the route handler from the caller's JWT identity.</summary>
    public string BuyerEmail { get; set; } = string.Empty;
}
