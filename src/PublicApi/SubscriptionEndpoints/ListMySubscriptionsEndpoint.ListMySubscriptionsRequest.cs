namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Server-assigned from the JWT; never taken from caller input.</summary>
    public string? ShopperEmail { get; set; }
}
