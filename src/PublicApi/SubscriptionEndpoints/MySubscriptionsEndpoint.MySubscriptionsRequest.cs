namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public ApplicationCore.Models.MaxioBilling.ShopperIdentity Shopper { get; init; }
        = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}